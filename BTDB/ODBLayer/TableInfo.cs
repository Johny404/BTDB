﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.Emit;
using BTDB.FieldHandler;
using BTDB.IL;
using BTDB.StreamLayer;

namespace BTDB.ODBLayer
{
    internal class TableInfo
    {
        readonly uint _id;
        readonly string _name;
        readonly ITableInfoResolver _tableInfoResolver;
        uint _clientTypeVersion;
        Type _clientType;
        readonly ConcurrentDictionary<uint, TableVersionInfo> _tableVersions = new ConcurrentDictionary<uint, TableVersionInfo>();
        Func<IInternalObjectDBTransaction, DBObjectMetadata, object> _creator;
        Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedWriter, object> _saver;
        readonly ConcurrentDictionary<uint, Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedReader, object>> _loaders = new ConcurrentDictionary<uint, Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedReader, object>>();
        ulong? _singletonOid;
        readonly object _singletonLock = new object();

        internal TableInfo(uint id, string name, ITableInfoResolver tableInfoResolver)
        {
            _id = id;
            _name = name;
            _tableInfoResolver = tableInfoResolver;
        }

        internal uint Id
        {
            get { return _id; }
        }

        internal string Name
        {
            get { return _name; }
        }

        internal Type ClientType
        {
            get { return _clientType; }
            set
            {
                _clientType = value;
                ClientTypeVersion = 0;
            }
        }

        internal TableVersionInfo ClientTableVersionInfo
        {
            get
            {
                TableVersionInfo tvi;
                if (_tableVersions.TryGetValue(_clientTypeVersion, out tvi)) return tvi;
                return null;
            }
        }

        internal uint LastPersistedVersion { get; set; }

        internal uint ClientTypeVersion
        {
            get { return _clientTypeVersion; }
            private set { _clientTypeVersion = value; }
        }

        internal Func<IInternalObjectDBTransaction, DBObjectMetadata, object> Creator
        {
            get
            {
                if (_creator == null) CreateCreator();
                return _creator;
            }
        }

        void CreateCreator()
        {
            var method = ILBuilder.Instance.NewMethod<Func<IInternalObjectDBTransaction, DBObjectMetadata, object>>(string.Format("Creator_{0}", Name));
            var ilGenerator = method.Generator;
            ilGenerator
                .Newobj(_clientType.GetConstructor(Type.EmptyTypes))
                .Ret();
            var creator = method.Create();
            System.Threading.Interlocked.CompareExchange(ref _creator, creator, null);
        }

        internal Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedWriter, object> Saver
        {
            get
            {
                if (_saver == null) CreateSaver();
                return _saver;
            }
        }

        public ulong SingletonOid
        {
            get
            {
                if (_singletonOid.HasValue) return _singletonOid.Value;
                _singletonOid = _tableInfoResolver.GetSingletonOid(_id);
                return _singletonOid.Value;
            }
        }

        public object SingletonLock
        {
            get
            {
                return _singletonLock;
            }
        }

        void CreateSaver()
        {
            var method = ILBuilder.Instance.NewMethod<Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedWriter, object>>(string.Format("Saver_{0}", Name));
            var ilGenerator = method.Generator;
            ilGenerator.DeclareLocal(ClientType);
            ilGenerator
                .Ldarg(3)
                .Castclass(ClientType)
                .Stloc(0);
            var anyNeedsCtx = ClientTableVersionInfo.NeedsCtx();
            if (anyNeedsCtx)
            {
                ilGenerator.DeclareLocal(typeof(IWriterCtx));
                ilGenerator
                    .Ldarg(0)
                    .Ldarg(2)
                    .Newobj(() => new DBWriterCtx(null, null))
                    .Stloc(1);
            }
            for (int i = 0; i < ClientTableVersionInfo.FieldCount; i++)
            {
                var field = ClientTableVersionInfo[i];
                var getter = ClientType.GetProperty(field.Name).GetGetMethod();
                Action<IILGen> writerOrCtx;
                if (field.Handler.NeedsCtx())
                    writerOrCtx = il => il.Ldloc(1);
                else
                    writerOrCtx = il => il.Ldarg(2);
                field.Handler.Save(ilGenerator, writerOrCtx, il =>
                    {
                        il.Ldloc(0).Callvirt(getter);
                        _tableInfoResolver.TypeConvertorGenerator.GenerateConversion(getter.ReturnType,
                                                                                     field.Handler.HandledType())(il);
                    });
            }
            ilGenerator
                .Ret();
            var saver = method.Create();
            System.Threading.Interlocked.CompareExchange(ref _saver, saver, null);
        }

        internal void EnsureClientTypeVersion()
        {
            if (ClientTypeVersion != 0) return;
            EnsureKnownLastPersistedVersion();
            var props = _clientType.GetProperties();
            var fields = new List<TableFieldInfo>(props.Length);
            foreach (var pi in props)
            {
                fields.Add(TableFieldInfo.Build(Name, pi, _tableInfoResolver.FieldHandlerFactory));
            }
            var tvi = new TableVersionInfo(fields.ToArray());
            if (LastPersistedVersion == 0)
            {
                _tableVersions.TryAdd(1, tvi);
                ClientTypeVersion = 1;
            }
            else
            {
                var last = _tableVersions.GetOrAdd(LastPersistedVersion, v => _tableInfoResolver.LoadTableVersionInfo(_id, v, Name));
                if (TableVersionInfo.Equal(last, tvi))
                {
                    _tableVersions[LastPersistedVersion] = tvi; // tvi was build from real types and not loaded so it is more exact
                    ClientTypeVersion = LastPersistedVersion;
                }
                else
                {
                    _tableVersions.TryAdd(LastPersistedVersion + 1, tvi);
                    ClientTypeVersion = LastPersistedVersion + 1;
                }
            }
        }

        void EnsureKnownLastPersistedVersion()
        {
            if (LastPersistedVersion != 0) return;
            LastPersistedVersion = _tableInfoResolver.GetLastPesistedVersion(_id);
        }

        internal Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedReader, object> GetLoader(uint version)
        {
            return _loaders.GetOrAdd(version, CreateLoader);
        }

        Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedReader, object> CreateLoader(uint version)
        {
            EnsureClientTypeVersion();
            var method = ILBuilder.Instance.NewMethod<Action<IInternalObjectDBTransaction, DBObjectMetadata, AbstractBufferedReader, object>>(string.Format("Loader_{0}_{1}", Name, version));
            var ilGenerator = method.Generator;
            ilGenerator.DeclareLocal(ClientType);
            ilGenerator
                .Ldarg(3)
                .Castclass(ClientType)
                .Stloc(0);
            var tableVersionInfo = _tableVersions.GetOrAdd(version, version1 => _tableInfoResolver.LoadTableVersionInfo(_id, version1, Name));
            var anyNeedsCtx = tableVersionInfo.NeedsCtx();
            if (anyNeedsCtx)
            {
                ilGenerator.DeclareLocal(typeof(IReaderCtx));
                ilGenerator
                    .Ldarg(0)
                    .Ldarg(2)
                    .Newobj(() => new DBReaderCtx(null, null))
                    .Stloc(1);
            }
            for (int fi = 0; fi < tableVersionInfo.FieldCount; fi++)
            {
                var srcFieldInfo = tableVersionInfo[fi];
                Action<IILGen> readerOrCtx;
                if (srcFieldInfo.Handler.NeedsCtx())
                    readerOrCtx = il => il.Ldloc(1);
                else
                    readerOrCtx = il => il.Ldarg(2);
                var destFieldInfo = ClientTableVersionInfo[srcFieldInfo.Name];
                if (destFieldInfo != null)
                {
                    var specializedSrcHandler = srcFieldInfo.Handler.SpecializeLoadForType(destFieldInfo.Handler.HandledType());
                    var willLoad = specializedSrcHandler.HandledType();
                    var fieldInfo = _clientType.GetProperty(destFieldInfo.Name).GetSetMethod();
                    var converterGenerator = _tableInfoResolver.TypeConvertorGenerator.GenerateConversion(willLoad, fieldInfo.GetParameters()[0].ParameterType);
                    if (converterGenerator != null)
                    {
                        ilGenerator.Ldloc(0);
                        specializedSrcHandler.Load(ilGenerator, readerOrCtx);
                        converterGenerator(ilGenerator);
                        ilGenerator.Call(fieldInfo);
                        continue;
                    }
                }
                srcFieldInfo.Handler.Skip(ilGenerator, readerOrCtx);
            }
            ilGenerator.Ret();
            return method.Create();
        }
    }
}