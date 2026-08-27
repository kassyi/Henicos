namespace Kassyi.GSharp.Core;

/// <summary>
/// Dummy structs to allow intellisense for CUDA built-in variables in C#.
/// The Transpiler will intercept these and replace them with CUDA equivalents.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public struct Dim3
{
    public int X => GpuOnlyException.Throw<int>();
    public int Y => GpuOnlyException.Throw<int>();
    public int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("threadIdx.{0}")]
public static class ThreadIndex
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("blockIdx.{0}")]
public static class BlockIndex
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("blockDim.{0}")]
public static class BlockDimension
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("gridDim.{0}")]
public static class GridDimension
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public static class Gpu
{
    [CudaIntrinsic("__syncthreads")]
    public static void SyncThreads() => GpuOnlyException.Throw();
    
    [CudaIntrinsic("cg::this_grid().sync")]
    public static void SyncGrid() => GpuOnlyException.Throw();

    /// <summary>
    /// A marker method used for JIT Constant Folding.
    /// The GSharp Transpiler will replace calls to this method with the raw macro string in the generated C++ code.
    /// </summary>
    public static int JitConstant(string macroName) => GpuOnlyException.Throw<int>();

    [CudaIntrinsic("atomicAdd")]
    public static int AtomicAdd(ref int address, int val) => GpuOnlyException.Throw<int>();
    [CudaIntrinsic("atomicAdd")]
    public static uint AtomicAdd(ref uint address, uint val) => GpuOnlyException.Throw<uint>();
    [CudaIntrinsic("atomicAdd")]
    public static ulong AtomicAdd(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();
    [CudaIntrinsic("atomicAdd")]
    public static float AtomicAdd(ref float address, float val) => GpuOnlyException.Throw<float>();

    [CudaIntrinsic("atomicSub")]
    public static int AtomicSub(ref int address, int val) => GpuOnlyException.Throw<int>();
    [CudaIntrinsic("atomicSub")]
    public static uint AtomicSub(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    [CudaIntrinsic("__gsharp_add_cc")]
    public static uint AddWithCarry(uint a, uint b, ref ulong carry) => GpuOnlyException.Throw<uint>();
    [CudaIntrinsic("__gsharp_sub_cc")]
    public static uint SubWithBorrow(uint a, uint b, ref long borrow) => GpuOnlyException.Throw<uint>();

    [CudaIntrinsic("atomicOr")]
    public static uint AtomicOr(ref uint address, uint val) => GpuOnlyException.Throw<uint>();
    [CudaIntrinsic("atomicOr")]
    public static ulong AtomicOr(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();
    
    [CudaIntrinsic("atomicMax")]
    public static uint AtomicMax(ref uint address, uint val) => GpuOnlyException.Throw<uint>();
    [CudaIntrinsic("atomicMax")]
    public static ulong AtomicMax(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    [CudaIntrinsic("__brev")]
    public static uint BitReverse(uint val) => GpuOnlyException.Throw<uint>();
    
    [CudaIntrinsic("__gsharp_mul_wide")]
    public static ulong MultiplyWide(uint a, uint b) => GpuOnlyException.Throw<ulong>();
    [CudaIntrinsic("__umul64hi")]
    public static ulong MultiplyHigh(ulong a, ulong b) => GpuOnlyException.Throw<ulong>();

    /// <summary>
    /// Injects raw PTX or CUDA C++ code directly into the generated output.
    /// Use string interpolation ($"") to reference C# variables safely.
    /// </summary>
    public static void InlinePtx(string ptx) => GpuOnlyException.Throw();

    /// <summary>
    /// A macro marker for fully unrolling a loop at Transpile-time.
    /// The G# Transpiler will emit the inner action body inline `count` times.
    /// </summary>
    public static void ForUnroll(int start, int count, Action<int> body) => GpuOnlyException.Throw();

    public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// A macro marker for fully unrolling a loop at Transpile-time, with context passing.
    /// </summary>
    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// A macro marker for generating a standard Grid-Stride Loop over a number of threads.
    /// </summary>
    public static void GridStrideLoop(int maxBounds, Action<int> body) => GpuOnlyException.Throw();

    public delegate void GridStrideAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// A macro marker for generating a standard Grid-Stride Loop over a number of threads, with context passing.
    /// </summary>
    public static void GridStrideLoop<TContext>(ref TContext ctx, int maxBounds, GridStrideAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// A macro marker for executing a block of code on a single thread (ThreadIndex.X == 0 && BlockIndex.X == 0).
    /// </summary>
    public static void SingleThread(Action body) => GpuOnlyException.Throw();
}
