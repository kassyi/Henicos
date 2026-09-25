namespace Kassyi.GSharp.Compiler;

/// <summary>Common type and namespace constants used across compiler transpilation.</summary>
/// <seealso cref="KnownAttributes"/>
internal static class TypeConstants
{
    public const string SystemNamespace = nameof(System);
    public const string NumericsNamespace = nameof(System.Numerics);
    public const string ThreadingNamespace = nameof(System.Threading);

    public const string SharedMemoryType = "SharedMemory";
    public const string GpuType = "Gpu";
    public const string CoreNamespace = "Kassyi.GSharp.Core";

    public const string MathType = nameof(Math);
    public const string MathFType = "MathF";
    public const string InterlockedType = nameof(Interlocked);
    public const string BitOperationsType = "BitOperations";

    public const string Vector2Type = nameof(System.Numerics.Vector2);
    public const string Vector3Type = nameof(System.Numerics.Vector3);
    public const string Vector4Type = nameof(System.Numerics.Vector4);

    public const string SpanType = nameof(Span<byte>);
    public const string ReadOnlySpanType = nameof(ReadOnlySpan<byte>);
    public const string ListType = nameof(List<object>);

    public static class GpuMacros
    {
        public const string Gpu = nameof(Gpu);
        public const string Hint = nameof(Hint);
        public const string ForUnroll = nameof(ForUnroll);
        public const string InlinePtx = nameof(InlinePtx);
        public const string SyncGrid = nameof(SyncGrid);
        public const string Unroll = nameof(Unroll);
    }

    public static class CudaTypes
    {
        public const string Void = "void";
        public const string Bool = "bool";
        public const string Char = "char";
        public const string UnsignedChar = "unsigned char";
        public const string Short = "short";
        public const string UnsignedShort = "unsigned short";
        public const string Int = "int";
        public const string UnsignedInt = "unsigned int";
        public const string LongLong = "long long";
        public const string UnsignedLongLong = "unsigned long long";
        public const string Float = "float";
        public const string Double = "double";

        public const string Float2 = "float2";
        public const string Float3 = "float3";
        public const string Float4 = "float4";
        public const string UnsignedIntPtr = "unsigned int*";
    }
}
