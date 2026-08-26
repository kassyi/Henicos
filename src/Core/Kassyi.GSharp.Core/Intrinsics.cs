[assembly: SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("ReSharper", "UnusedType.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("IDE", "IDE0051", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("Compiler", "CS0169", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("Compiler", "CS0414", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("Performance", "CA1822", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]

namespace Kassyi.GSharp.Core;

/// <summary>
/// Dummy structs to allow intellisense for CUDA built-in variables in C#.
/// The Transpiler will intercept these and replace them with CUDA equivalents.
/// </summary>
[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public struct Dim3
{
    public int X => GpuOnlyException.Throw<int>();
    public int Y => GpuOnlyException.Throw<int>();
    public int Z => GpuOnlyException.Throw<int>();
}

public static class ThreadIndex
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

public static class BlockIndex
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

public static class BlockDimension
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

public static class GridDimension
{
    public static int X => GpuOnlyException.Throw<int>();
    public static int Y => GpuOnlyException.Throw<int>();
    public static int Z => GpuOnlyException.Throw<int>();
}

[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public static class Gpu
{
    public static void SyncThreads() => GpuOnlyException.Throw();
    public static void SyncGrid() => GpuOnlyException.Throw();

    /// <summary>
    /// A marker method used for JIT Constant Folding.
    /// The GSharp Transpiler will replace calls to this method with the raw macro string in the generated C++ code.
    /// </summary>
    public static int JitConstant(string macroName) => GpuOnlyException.Throw<int>();

    public static int AtomicAdd(ref int address, int val) => GpuOnlyException.Throw<int>();
    public static uint AtomicAdd(ref uint address, uint val) => GpuOnlyException.Throw<uint>();
    public static ulong AtomicAdd(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();
    public static float AtomicAdd(ref float address, float val) => GpuOnlyException.Throw<float>();

    public static int AtomicSub(ref int address, int val) => GpuOnlyException.Throw<int>();
    public static uint AtomicSub(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    public static uint AddWithCarry(uint a, uint b, ref ulong carry) => GpuOnlyException.Throw<uint>();
    public static uint SubWithBorrow(uint a, uint b, ref long borrow) => GpuOnlyException.Throw<uint>();

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

    /// <summary>
    /// A macro marker for executing a block of code on a single thread (ThreadIndex.X == 0 && BlockIndex.X == 0).
    /// </summary>
    public static void SingleThread(Action body) => GpuOnlyException.Throw();
}
