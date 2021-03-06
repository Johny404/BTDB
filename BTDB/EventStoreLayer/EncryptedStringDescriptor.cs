using System;
using System.Collections.Generic;
using System.Text;
using BTDB.Encrypted;
using BTDB.IL;

namespace BTDB.EventStoreLayer
{
    public class EncryptedStringDescriptor : ITypeDescriptor
    {
        public string Name => "EncryptedString";

        public bool FinishBuildFromType(ITypeDescriptorFactory factory)
        {
            return true;
        }

        public void BuildHumanReadableFullName(StringBuilder text, HashSet<ITypeDescriptor> stack, uint indent)
        {
            text.Append(Name);
        }

        public bool Equals(ITypeDescriptor other, HashSet<ITypeDescriptor> stack)
        {
            return ReferenceEquals(this, other);
        }

        public Type GetPreferedType() => typeof(EncryptedString);

        public Type GetPreferedType(Type targetType)
        {
            return GetPreferedType();
        }

        public ITypeNewDescriptorGenerator? BuildNewDescriptorGenerator()
        {
            return null;
        }

        public ITypeDescriptor? NestedType(int index)
        {
            return null;
        }

        public void MapNestedTypes(Func<ITypeDescriptor, ITypeDescriptor> map)
        {
        }

        public bool Sealed => true;

        public bool StoredInline => true;

        public bool LoadNeedsHelpWithConversion => true;

        public void ClearMappingToType()
        {
        }

        public bool ContainsField(string name)
        {
            return false;
        }

        public bool AnyOpNeedsCtx() => true;

        public void GenerateLoad(IILGen ilGenerator, Action<IILGen> pushReader, Action<IILGen> pushCtx,
            Action<IILGen> pushDescriptor, Type targetType)
        {
            pushCtx(ilGenerator);
            ilGenerator.Callvirt(() => ((ITypeBinaryDeserializerContext) null).LoadEncryptedString());
            if (targetType != typeof(object))
            {
                if (targetType != GetPreferedType())
                    throw new ArgumentOutOfRangeException(nameof(targetType));
                return;
            }

            ilGenerator.Box(GetPreferedType());
        }

        public void GenerateSkip(IILGen ilGenerator, Action<IILGen> pushReader, Action<IILGen> pushCtx)
        {
            pushCtx(ilGenerator);
            ilGenerator.Callvirt(() => ((ITypeBinaryDeserializerContext) null).SkipEncryptedString());
        }

        public void GenerateSave(IILGen ilGenerator, Action<IILGen> pushWriter, Action<IILGen> pushCtx,
            Action<IILGen> pushValue, Type valueType)
        {
            pushCtx(ilGenerator);
            pushValue(ilGenerator);
            ilGenerator.Callvirt(() => ((ITypeBinarySerializerContext) null).StoreEncryptedString(default));
        }

        public bool Equals(ITypeDescriptor other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public ITypeDescriptor CloneAndMapNestedTypes(ITypeDescriptorCallbacks typeSerializers,
            Func<ITypeDescriptor, ITypeDescriptor> map)
        {
            return this;
        }
    }
}
