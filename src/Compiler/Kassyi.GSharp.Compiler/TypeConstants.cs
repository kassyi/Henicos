namespace Kassyi.GSharp.Compiler;

/// <summary>Common type and namespace constants used across compiler transpilation.</summary>
/// <seealso cref="KnownAttributes"/>
internal static class TypeConstants
{
    public const string SystemNamespace = "System";
    public const string NumericsNamespace = "Numerics";
    public const string ThreadingNamespace = "Threading";

    public const string SharedMemoryType = "SharedMemory";
    public const string GpuType = "Gpu";
    public const string CoreNamespace = "Kassyi.GSharp.Core";

    public const string MathType = "Math";
    public const string MathFType = "MathF";
    public const string InterlockedType = "Interlocked";

    public const string Vector2Type = "Vector2";
    public const string Vector3Type = "Vector3";
    public const string Vector4Type = "Vector4";
    public const string GpuBigIntRefType = "GpuBigIntRef";

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
