// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
#pragma warning disable IDE0051 // Remove unused private members
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

    public static int AtomicAdd(ref int address, int val) => GpuOnlyException.Throw<int>();
    public static float AtomicAdd(ref float address, float val) => GpuOnlyException.Throw<float>();
}
