using System;

namespace BTDB.Serialization;

public class ClassMetadata
{
    public Type Type;
    public string Name;
    public string Namespace;
    public string? PersistedName;
    public FieldMetadata[] Fields;
    public unsafe delegate*<object> Creator;
}
