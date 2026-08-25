using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]
[assembly: SuppressMessage("ReSharper", "UnusedType.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Core.Intrinsics", Justification = "Public API exposed for library consumers")]

#pragma warning disable CA1822 // Mark members as static

namespace Kassyi.GSharp.Core;

/// <summary>
/// Dummy structs to allow intellisense for CUDA built-in variables in C#.
/// The Transpiler will intercept these and replace them with CUDA equivalents.
/// </summary>
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
}
