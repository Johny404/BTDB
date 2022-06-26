using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BTDB.Buffer;
using BTDB.Collections;

namespace BTDB.Bon;

public enum BonType
{
    Error, // 0
    Null, // 1
    Undefined, // 2
    Bool, // 3 - false, 4 - true
    Integer, // 5 - 0, ... 15 - 10, 16 - -1, ... 25 - -10, 128 - VUlong, 129 - VUlong (*-1)
    Float, // 64 - half, 65 - float, 66 - double
    String, // 26 - empty, 130 - VUint offset to VUint len of UTF-8 bytes and UTF-8 bytes themself
    DateTime, // 67 - (DateTime.Ticks in 8 bytes(long))
    Guid, // 68 - 16 bytes
    Array, // 27 - empty, 131 - VUint offset to VUint len + Bons*len
    Object, // 28 - empty, 132 - VUint offset to VUint offset to VUint len + VUint offsets to strings, Bons*len
    Class, // 133 - VUint offset to VUint offset to VUint len + VUint offset to type name string + VUint offsets to strings, Bons*len
    Dictionary, // 29 - empty, 134 - VUint offset to VUint len + (Key Bon + Value Bon)*len
}

public static class Helpers
{
    public const byte CodeError = 0;
    public const byte CodeNull = 1;
    public const byte CodeUndefined = 2;
    public const byte CodeFalse = 3;
    public const byte CodeTrue = 4;
    public const byte Code0 = 5;
    public const byte CodeM1 = 16;
    public const byte CodeM10 = 25;
    public const byte CodeInteger = 128;
    public const byte CodeMInteger = 129;
    public const byte CodeHalf = 64;
    public const byte CodeFloat = 65;
    public const byte CodeDouble = 66;
    public const byte CodeStringEmpty = 26;
    public const byte CodeStringPtr = 130;
    public const byte CodeDateTime = 67;
    public const byte CodeGuid = 68;
    public const byte CodeArrayEmpty = 27;
    public const byte CodeArrayPtr = 131;
    public const byte CodeObjectEmpty = 28;
    public const byte CodeObjectPtr = 29;
    public const byte CodeClassPtr = 133;
    public const byte CodeDictionaryEmpty = 29;
    public const byte CodeDictionaryPtr = 134;

    public static BonType BonTypeFromByte(byte b)
    {
        return b switch
        {
            1 => BonType.Null,
            2 => BonType.Undefined,
            3 or 4 => BonType.Bool,
            >= 5 and <= 25 or 128 or 129 => BonType.Integer,
            64 or 65 or 66 => BonType.Float,
            26 or 130 => BonType.String,
            67 => BonType.DateTime,
            68 => BonType.Guid,
            27 or 131 => BonType.Array,
            28 or 132 => BonType.Object,
            133 => BonType.Class,
            29 or 134 => BonType.Dictionary,
            _ => BonType.Error
        };
    }

    static void Reserve(ref byte[] data, uint len)
    {
        Array.Resize(ref data, (int)Math.Min((uint)Array.MaxLength, Math.Max(data.Length * 2, len)));
    }

    public static Span<byte> WriteBlock(ref byte[] data, ref uint pos, uint len)
    {
        if (pos + len > data.Length)
        {
            Reserve(ref data, pos + len);
        }

        var res = data.AsSpan((int)pos, (int)len);
        pos += len;
        return res;
    }

    public static void WriteByte(ref byte[] data, ref uint pos, byte value)
    {
        if (pos >= data.Length)
        {
            Reserve(ref data, pos + 1);
        }

        data[pos++] = value;
    }

    public static void WriteUtf8String(ref byte[] data, ref uint pos, ReadOnlySpan<char> value)
    {
        var l = Encoding.UTF8.GetByteCount(value);
        WriteVUInt32(ref data, ref pos, (uint)l);
        Encoding.UTF8.GetBytes(value, WriteBlock(ref data, ref pos, (uint)l));
    }

    public static void WriteVUInt32(ref byte[] data, ref uint pos, uint value)
    {
        var len = PackUnpack.LengthVUInt(value);
        PackUnpack.UnsafePackVUInt(ref MemoryMarshal.GetReference(WriteBlock(ref data, ref pos, len)), value, len);
    }

    public static void WriteVUInt64(ref byte[] data, ref uint pos, ulong value)
    {
        var len = PackUnpack.LengthVUInt(value);
        PackUnpack.UnsafePackVUInt(ref MemoryMarshal.GetReference(WriteBlock(ref data, ref pos, len)), value, len);
    }

    public static void Write(ref byte[] data, ref uint pos, bool value)
    {
        WriteByte(ref data, ref pos, value ? CodeTrue : CodeFalse);
    }

    public static void Write(ref byte[] data, ref uint pos, double value)
    {
        var f = (float)value;
        if (f == value)
        {
            var h = (Half)value;
            if ((double)h == value)
            {
                WriteByte(ref data, ref pos, CodeHalf);
                MemoryMarshal.Write(WriteBlock(ref data, ref pos, 2),
                    ref Unsafe.AsRef(PackUnpack.AsLittleEndian(Unsafe.As<Half, ushort>(ref Unsafe.AsRef(h)))));
            }
            else
            {
                WriteByte(ref data, ref pos, CodeFloat);
                MemoryMarshal.Write(WriteBlock(ref data, ref pos, 4),
                    ref Unsafe.AsRef(PackUnpack.AsLittleEndian(Unsafe.As<float, uint>(ref Unsafe.AsRef(f)))));
            }
        }
        else
        {
            WriteByte(ref data, ref pos, CodeDouble);
            MemoryMarshal.Write(WriteBlock(ref data, ref pos, 8),
                ref Unsafe.AsRef(PackUnpack.AsLittleEndian(Unsafe.As<double, ulong>(ref Unsafe.AsRef(value)))));
        }
    }

    public static uint CalcStartOffsetOfBon(ReadOnlySpan<byte> bon)
    {
        return (uint)bon.Length - bon[^1] - 1;
    }

    public static uint SkipItemOfBon(ReadOnlySpan<byte> bon, uint ofs)
    {
        var b = bon[(int)ofs++];
        switch (b)
        {
            case < 64:
                break;
            case >= 128:
                ofs += PackUnpack.LengthVUIntByFirstByte(bon[(int)ofs]);
                break;
            case 64:
                ofs += 2;
                break;
            case 65:
                ofs += 4;
                break;
            case 66 or 67:
                ofs += 8;
                break;
            case 68:
                ofs += 16;
                break;
        }

        return ofs;
    }

    public static bool TryGetDouble(ReadOnlySpan<byte> bon, ref uint ofs, out double value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case >= Code0 and <= CodeM10:
            {
                ofs++;
                value = b < CodeM1 ? b - Code0 : CodeM1 - b - 1;
                return true;
            }
            case CodeInteger:
            {
                ofs++;
                var data = bon[(int)ofs..];
                var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
                if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
                // All range checks were done already before, so now do it without them for speed
                value = PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
                ofs += len;
                return true;
            }
            case CodeMInteger:
            {
                ofs++;
                var data = bon[(int)ofs..];
                var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
                if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
                // All range checks were done already before, so now do it without them for speed
                value = -(double)PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
                ofs += len;
                return true;
            }
            case CodeHalf:
            {
                ofs++;
                Half h = new();
                Unsafe.As<Half, ushort>(ref Unsafe.AsRef(h)) =
                    PackUnpack.AsLittleEndian(MemoryMarshal.Read<ushort>(bon[(int)ofs..]));
                value = (double)h;
                ofs += 2;
                return true;
            }
            case CodeFloat:
            {
                ofs++;
                float f = new();
                Unsafe.As<float, uint>(ref Unsafe.AsRef(f)) =
                    PackUnpack.AsLittleEndian(MemoryMarshal.Read<uint>(bon[(int)ofs..]));
                value = f;
                ofs += 4;
                return true;
            }
            case CodeDouble:
            {
                ofs++;
                value = 0;
                Unsafe.As<double, ulong>(ref Unsafe.AsRef(value)) =
                    PackUnpack.AsLittleEndian(MemoryMarshal.Read<ulong>(bon[(int)ofs..]));
                ofs += 8;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static bool TryGetDateTime(ReadOnlySpan<byte> bon, ref uint ofs, out DateTime value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case CodeDateTime:
            {
                ofs++;
                value = DateTime.FromBinary(
                    (long)PackUnpack.AsLittleEndian(MemoryMarshal.Read<ulong>(bon[(int)ofs..])));
                ofs += 8;
                return true;
            }
        }

        value = new();
        return false;
    }

    public static bool TryGetGuid(ReadOnlySpan<byte> bon, ref uint ofs, out Guid value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case CodeGuid:
            {
                ofs++;
                value = new(bon[(int)ofs..((int)ofs + 16)]);
                ofs += 16;
                return true;
            }
        }

        value = new();
        return false;
    }

    public static bool TryGetString(ReadOnlySpan<byte> bon, ref uint ofs, out string value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case CodeStringEmpty:
            {
                ofs++;
                value = "";
                return true;
            }
            case CodeStringPtr:
            {
                ofs++;
                var strOfs = ReadVUInt(bon, ref ofs);
                value = ReadUtf8WithVUintLen(bon, strOfs);
                return true;
            }
        }

        value = "";
        return false;
    }

    public static bool TryGetLong(ReadOnlySpan<byte> bon, ref uint ofs, out long value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case >= Code0 and <= CodeM10:
            {
                ofs++;
                value = b < CodeM1 ? b - Code0 : CodeM1 - b - 1;
                return true;
            }
            case CodeInteger:
            {
                ofs++;
                var data = bon[(int)ofs..];
                var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
                if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
                // All range checks were done already before, so now do it without them for speed
                var v = PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
                if (v > long.MaxValue)
                {
                    ofs--;
                    value = 0;
                    return false;
                }

                value = (long)v;
                ofs += len;
                return true;
            }
            case CodeMInteger:
            {
                ofs++;
                var data = bon[(int)ofs..];
                var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
                if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
                // All range checks were done already before, so now do it without them for speed
                var v = -(long)PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
                if (v >= 0)
                {
                    value = 0;
                    ofs--;
                    return false;
                }

                value = v;
                ofs += len;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static bool TryGetULong(ReadOnlySpan<byte> bon, ref uint ofs, out ulong value)
    {
        var b = bon[(int)ofs];
        switch (b)
        {
            case >= Code0 and < CodeM1:
            {
                ofs++;
                value = (ulong)(b - Code0);
                return true;
            }
            case CodeInteger:
            {
                ofs++;
                var data = bon[(int)ofs..];
                var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
                if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
                // All range checks were done already before, so now do it without them for speed
                value = PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
                ofs += len;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static string ReadUtf8WithVUintLen(ReadOnlySpan<byte> bon, uint ofs)
    {
        var len = ReadVUInt(bon, ref ofs);
        return Encoding.UTF8.GetString(bon[(int)ofs..(int)(ofs + len)]);
    }

    public static uint ReadVUInt(ReadOnlySpan<byte> bon, ref uint ofs)
    {
        var data = bon[(int)ofs..];
        var len = PackUnpack.LengthVUIntByFirstByte(data[0]);
        ofs += len;
        if ((uint)data.Length < len) PackUnpack.ThrowEndOfStreamException();
        // All range checks were done already before, so now do it without them for speed
        return (uint)PackUnpack.UnsafeUnpackVUInt(ref MemoryMarshal.GetReference(data), len);
    }
}

public struct BonBuilder
{
    enum State
    {
        Empty,
        Full,
        Finished,
        Array,
        ObjectKey,
        ObjectValue,
        ClassKey,
        ClassValue,
        DictionaryKey,
        DictionaryValue,
    };

    State _state = State.Empty;
    byte[] _rootData = Array.Empty<byte>();
    uint _rootPos = 0;
    uint _lastBonPos = 0;
    StructList<(StructList<uint>, byte[], uint, uint, State)> _stack = new();
    StructList<uint> _objKeys = new();
    byte[] _topData;
    uint _topPos = 0;
    uint _items = 0;
    readonly Dictionary<string, uint> _strCache = new();

    public BonBuilder()
    {
        _topData = _rootData;
    }

    public void Write(string? value)
    {
        BeforeBon();
        BasicWriteString(ref _topData, ref _topPos, out _lastBonPos, value);
        AfterBon();
    }

    public void Write(bool value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        Helpers.Write(ref _topData, ref _topPos, value);
        AfterBon();
    }

    public void Write(double value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        Helpers.Write(ref _topData, ref _topPos, value);
        AfterBon();
    }

    public void Write(long value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        switch (value)
        {
            case >= 0 and <= 10:
                Helpers.WriteByte(ref _topData, ref _topPos, (byte)(Helpers.Code0 + (int)value));
                break;
            case >= -10 and <= -1:
                Helpers.WriteByte(ref _topData, ref _topPos, (byte)(Helpers.CodeM1 - 1 + (int)-value));
                break;
            case >= 0:
                Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeInteger);
                Helpers.WriteVUInt64(ref _topData, ref _topPos, (ulong)value);
                break;
            default:
                Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeMInteger);
                Helpers.WriteVUInt64(ref _topData, ref _topPos, (ulong)-value);
                break;
        }

        AfterBon();
    }

    public void Write(ulong value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        switch (value)
        {
            case >= 0 and <= 10:
                Helpers.WriteByte(ref _topData, ref _topPos, (byte)(Helpers.Code0 + (int)value));
                break;
            default:
                Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeInteger);
                Helpers.WriteVUInt64(ref _topData, ref _topPos, value);
                break;
        }

        AfterBon();
    }

    public void WriteNull()
    {
        BeforeBon();
        _lastBonPos = _topPos;
        Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeNull);
        AfterBon();
    }

    public void WriteUndefined()
    {
        if (_state == State.Empty)
        {
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeUndefined);
            _items++;
            if (_state == State.Empty) _state = State.Full;
        }
        else
        {
            ThrowWrongState();
        }
    }

    public void Write(DateTime value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeDateTime);
        var v = value.ToBinary();
        MemoryMarshal.Write(Helpers.WriteBlock(ref _rootData, ref _rootPos, 8),
            ref Unsafe.AsRef(PackUnpack.AsLittleEndian(Unsafe.As<long, ulong>(ref Unsafe.AsRef(v)))));
        AfterBon();
    }

    public void Write(Guid value)
    {
        BeforeBon();
        _lastBonPos = _topPos;
        Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeGuid);
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Guid, byte>(ref Unsafe.AsRef(value)), 16)
            .CopyTo(Helpers.WriteBlock(ref _rootData, ref _rootPos, 16));
        AfterBon();
    }

    public void StartArray()
    {
        BeforeBon();
        _state = State.Array;
        StackPush();
    }

    public void FinishArray()
    {
        if (_state != State.Array) ThrowWrongState();

        var items = _items;
        var bytes = _topData[..(int)_topPos];
        StackPop();
        if (items == 0)
        {
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeArrayEmpty);
        }
        else
        {
            _rootPos = _stack.Count == 0 ? _topPos : _stack[0].Item3;
            var pos = _rootPos;
            Helpers.WriteVUInt32(ref _rootData, ref _rootPos, items);
            bytes.CopyTo(Helpers.WriteBlock(ref _rootData, ref _rootPos, (uint)bytes.Length));
            if (_stack.Count == 0) _topPos = _rootPos;
            else _stack[0].Item3 = _rootPos;
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeArrayPtr);
            Helpers.WriteVUInt32(ref _topData, ref _topPos, pos);
        }

        AfterBon();
    }

    public void StartObject()
    {
        BeforeBon();
        _state = State.ObjectKey;
        StackPush();
    }

    public void WriteKey(string name)
    {
        if (_state is not State.ObjectKey and not State.ClassKey) ThrowWrongState();
        _objKeys.Add(WriteDedupString(name));
        _state = _state == State.ObjectKey ? State.ObjectValue : State.ClassValue;
    }

    public void FinishObject()
    {
        if (_state != State.ObjectKey) ThrowWrongState();

        var items = _items;
        var objKeys = _objKeys;
        var bytes = _topData[..(int)_topPos];
        StackPop();
        if (items == 0)
        {
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeObjectEmpty);
        }
        else
        {
            _rootPos = _stack.Count == 0 ? _topPos : _stack[0].Item3;
            var posKeys = _rootPos;
            Helpers.WriteVUInt32(ref _rootData, ref _rootPos, items);
            foreach (var keyOfs in objKeys)
            {
                Helpers.WriteVUInt32(ref _rootData, ref _rootPos, keyOfs);
            }

            var posKeys2 = _rootData.AsSpan(0, (int)posKeys)
                .IndexOf(_rootData.AsSpan((int)posKeys, (int)_rootPos - (int)posKeys));
            if (posKeys2 >= 0)
            {
                _rootPos = posKeys;
                posKeys = (uint)posKeys2;
            }

            var pos = _rootPos;
            Helpers.WriteVUInt32(ref _rootData, ref _rootPos, posKeys);
            bytes.CopyTo(Helpers.WriteBlock(ref _rootData, ref _rootPos, (uint)bytes.Length));
            if (_stack.Count == 0) _topPos = _rootPos;
            else _stack[0].Item3 = _rootPos;
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeObjectPtr);
            Helpers.WriteVUInt32(ref _topData, ref _topPos, pos);
        }

        AfterBon();
    }

    public void StartClass(string name)
    {
        BeforeBon();
        _state = State.ObjectKey;
        _objKeys.Add(WriteDedupString(name));
        StackPush();
    }

    public void FinishClass()
    {
        if (_state != State.Array) ThrowWrongState();

        var items = _items;
        var objKeys = _objKeys;
        var bytes = _topData[..(int)_topPos];
        StackPop();
        _rootPos = _stack.Count == 0 ? _topPos : _stack[0].Item3;
        var posKeys = _rootPos;
        Helpers.WriteVUInt32(ref _rootData, ref _rootPos, items);
        foreach (var keyOfs in objKeys)
        {
            Helpers.WriteVUInt32(ref _rootData, ref _rootPos, keyOfs);
        }

        var posKeys2 = _rootData.AsSpan(0, (int)posKeys)
            .IndexOf(_rootData.AsSpan((int)posKeys, (int)_rootPos - (int)posKeys));
        if (posKeys2 >= 0)
        {
            _rootPos = posKeys;
            posKeys = (uint)posKeys2;
        }

        var pos = _rootPos;
        Helpers.WriteVUInt32(ref _rootData, ref _rootPos, posKeys);
        if (bytes.Length > 0)
            bytes.CopyTo(Helpers.WriteBlock(ref _rootData, ref _rootPos, (uint)bytes.Length));
        if (_stack.Count == 0) _topPos = _rootPos;
        else _stack[0].Item3 = _rootPos;
        _lastBonPos = _topPos;
        Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeClassPtr);
        Helpers.WriteVUInt32(ref _topData, ref _topPos, pos);

        AfterBon();
    }

    public void StartDictionary()
    {
        BeforeBon();
        _state = State.DictionaryKey;
        StackPush();
    }

    public void FinishDictionary()
    {
        if (_state != State.DictionaryKey) ThrowWrongState();

        var items = _items;
        var bytes = _topData[..(int)_topPos];
        StackPop();
        if (items == 0)
        {
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeDictionaryEmpty);
        }
        else
        {
            _rootPos = _stack.Count == 0 ? _topPos : _stack[0].Item3;
            var pos = _rootPos;
            Helpers.WriteVUInt32(ref _rootData, ref _rootPos, items / 2);
            bytes.CopyTo(Helpers.WriteBlock(ref _rootData, ref _rootPos, (uint)bytes.Length));
            if (_stack.Count == 0) _topPos = _rootPos;
            else _stack[0].Item3 = _rootPos;
            _lastBonPos = _topPos;
            Helpers.WriteByte(ref _topData, ref _topPos, Helpers.CodeDictionaryPtr);
            Helpers.WriteVUInt32(ref _topData, ref _topPos, pos);
        }

        AfterBon();
    }

    void StackPush()
    {
        _stack.Add((_objKeys, _topData, _topPos, _items, _state));
        _objKeys = new();
        _topData = Array.Empty<byte>();
        _topPos = 0;
        _items = 0;
    }

    void StackPop()
    {
        (_objKeys, _topData, _topPos, _items, _state) = _stack.Last;
        _stack.Pop();
    }

    void AfterBon()
    {
        _items++;
        _state = _state switch
        {
            State.Empty => State.Full,
            State.ObjectValue => State.ObjectKey,
            State.ClassValue => State.ClassKey,
            State.DictionaryKey => State.DictionaryValue,
            State.DictionaryValue => State.DictionaryKey,
            _ => _state
        };
    }

    void BeforeBon()
    {
        if (_state is not (State.Empty or State.Array or State.ObjectValue or State.ClassValue or State.DictionaryKey
            or State.DictionaryValue))
        {
            ThrowWrongState();
        }
    }

    void BasicWriteString(ref byte[] data, ref uint pos, out uint bonPos, string? value)
    {
        if (value == null)
        {
            bonPos = pos;
            Helpers.WriteByte(ref data, ref pos, Helpers.CodeNull);
        }
        else if (value.Length == 0)
        {
            bonPos = pos;
            Helpers.WriteByte(ref data, ref pos, Helpers.CodeStringEmpty);
        }
        else
        {
            var ofs = WriteDedupString(value);
            bonPos = pos;
            Helpers.WriteByte(ref data, ref pos, Helpers.CodeStringPtr);
            Helpers.WriteVUInt32(ref data, ref pos, ofs);
        }
    }

    void ThrowWrongState()
    {
        throw new InvalidOperationException("State " + _state + " is not valid for this operation");
    }

    uint WriteDedupString(string value)
    {
        if (_strCache.TryGetValue(value, out var pos))
        {
            return pos;
        }

        _rootPos = _stack.Count == 0 ? _topPos : _stack[0].Item3;
        pos = _rootPos;
        Helpers.WriteUtf8String(ref _rootData, ref _rootPos, value);
        if (_stack.Count == 0) _topPos = _rootPos;
        else _stack[0].Item3 = _rootPos;
        _strCache[value] = pos;
        return pos;
    }

    public ByteBuffer Finish()
    {
        if (_state == State.Full)
        {
            _rootPos = _topPos;
            Helpers.WriteByte(ref _rootData, ref _rootPos, (byte)(_rootPos - _lastBonPos));
            _state = State.Finished;
        }

        if (_state != State.Finished) ThrowWrongState();
        return ByteBuffer.NewSync(_rootData, 0, (int)_rootPos);
    }
}

public ref struct Bon
{
    ReadOnlySpan<byte> _buf;
    uint _ofs;
    uint _items;

    public Bon(byte[] buf)
    {
        _buf = buf;
        _ofs = Helpers.CalcStartOffsetOfBon(_buf);
        _items = 1;
    }

    public Bon(ByteBuffer buf)
    {
        _buf = buf.AsSyncReadOnlySpan();
        _ofs = Helpers.CalcStartOffsetOfBon(_buf);
        _items = 1;
    }

    public Bon(ReadOnlySpan<byte> buf, uint ofs, uint items)
    {
        _buf = buf;
        _ofs = ofs;
        _items = items;
    }

    public BonType BonType
    {
        get
        {
            Debug.Assert(_items > 0);
            return Helpers.BonTypeFromByte(_buf[(int)_ofs]);
        }
    }

    public bool TryGetDouble(out double value)
    {
        return ItemConsumed(Helpers.TryGetDouble(_buf, ref _ofs, out value));
    }

    bool ItemConsumed(bool result)
    {
        if (result) _items--;
        return result;
    }

    public bool Eof => _items == 0;

    public void Skip()
    {
        Debug.Assert(_items > 0);
        _ofs = Helpers.SkipItemOfBon(_buf, _ofs);
        _items--;
    }

    public bool TryGetLong(out long value)
    {
        return ItemConsumed(Helpers.TryGetLong(_buf, ref _ofs, out value));
    }

    public bool TryGetULong(out ulong value)
    {
        return ItemConsumed(Helpers.TryGetULong(_buf, ref _ofs, out value));
    }

    public bool TryGetDateTime(out DateTime value)
    {
        return ItemConsumed(Helpers.TryGetDateTime(_buf, ref _ofs, out value));
    }

    public bool TryGetGuid(out Guid value)
    {
        return ItemConsumed(Helpers.TryGetGuid(_buf, ref _ofs, out value));
    }

    public bool TryGetString(out string value)
    {
        return ItemConsumed(Helpers.TryGetString(_buf, ref _ofs, out value));
    }

    public bool TryGetBool(out bool value)
    {
        value = false;
        var b = _buf[(int)_ofs];
        switch (b)
        {
            case Helpers.CodeFalse:
                _ofs++;
                _items--;
                return true;
            case Helpers.CodeTrue:
                value = true;
                _ofs++;
                _items--;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetUndefined()
    {
        if (_buf[(int)_ofs] == Helpers.CodeUndefined)
        {
            _ofs++;
            _items--;
            return true;
        }

        return false;
    }

    public bool TryGetNull()
    {
        if (_buf[(int)_ofs] == Helpers.CodeNull)
        {
            _ofs++;
            _items--;
            return true;
        }

        return false;
    }

    public bool TryGetArray(out Bon bon)
    {
        var b = _buf[(int)_ofs];
        switch (b)
        {
            case Helpers.CodeArrayEmpty:
                _ofs++;
                _items--;
                bon = new(new(), 0, 0);
                return true;
            case Helpers.CodeArrayPtr:
            {
                _ofs++;
                _items--;
                var ofs = Helpers.ReadVUInt(_buf, ref _ofs);
                var items = Helpers.ReadVUInt(_buf, ref ofs);
                bon = new(_buf, ofs, items);
                return true;
            }
            default:
                bon = new(new(), 0, 0);
                return false;
        }
    }
}
