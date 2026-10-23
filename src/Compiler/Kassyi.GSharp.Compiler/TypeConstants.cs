using System.Numerics;
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

    public const string Vector2Type = nameof(Vector2);
    public const string Vector3Type = nameof(Vector3);
    public const string Vector4Type = nameof(Vector4);

    public const string SpanType = nameof(Span<byte>);
    public const string ReadOnlySpanType = nameof(ReadOnlySpan<byte>);
    public const string ListType = nameof(List<object>);
    public const string WarpType = "Warp";

    /// <summary><c>Gpu.Unsafe</c>, the low-level device memory intrinsics.</summary>
    public static class GpuUnsafe
    {
        public const string Type = "Unsafe";
        public const string Malloc = nameof(Malloc);
    }

    public static class GpuMacros
    {
        public const string Hint = nameof(Hint);
        public const string ForUnroll = nameof(ForUnroll);
        public const string InlinePtx = nameof(InlinePtx);
        public const string SyncGrid = nameof(SyncGrid);
        public const string Unroll = nameof(Unroll);
        public const string GridStrideLoop = nameof(GridStrideLoop);
        public const string SingleThread = nameof(SingleThread);
        public const string Constant = nameof(Constant);
    }
}
