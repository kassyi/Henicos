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
    public const string WarpType = "Warp";
    public const string WmmaType = "Wmma";
    public const string ThreadIndexType = "ThreadIndex";
    public const string BlockIndexType = "BlockIndex";
    public const string SyncThreadsMethod = "SyncThreads";
    public const string ActiveMaskMethod = "ActiveMask";

    /// <summary>WMMA fragment, shape marker, layout marker, and method names in <c>Kassyi.GSharp.Core</c>.</summary>
    public static class WmmaTypes
    {
        public const string MatrixA = "WmmaMatrixA";
        public const string MatrixB = "WmmaMatrixB";
        public const string Accumulator = "WmmaAccumulator";

        public const string ShapeInterface = "IWmmaShape";
        public const string M16N16K16 = "WmmaM16N16K16";
        public const string M32N8K16 = "WmmaM32N8K16";
        public const string M8N32K16 = "WmmaM8N32K16";
        public const string M8N8K4 = "WmmaM8N8K4";

        public const string LayoutInterface = "IWmmaLayout";
        public const string RowMajor = "WmmaRowMajor";
        public const string ColumnMajor = "WmmaColumnMajor";

        public const string MemoryLayout = "WmmaMemoryLayout";

        public const string Fill = "Fill";
        public const string Load = "Load";
        public const string Store = "Store";
        public const string MultiplyAccumulate = "MultiplyAccumulate";
    }
    public const string GCHandleType = nameof(System.Runtime.InteropServices.GCHandle);

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
        public const string CopyAsync = nameof(CopyAsync);
        public const string CopyAsyncWaitGroup = nameof(CopyAsyncWaitGroup);
    }
}
