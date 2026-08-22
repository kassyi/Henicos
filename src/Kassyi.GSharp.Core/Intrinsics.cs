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
    private const string Msg = "This property can only be executed on the GPU.";
    public int X => throw new InvalidOperationException(Msg);
    public int Y => throw new InvalidOperationException(Msg);
    public int Z => throw new InvalidOperationException(Msg);
}

public static class ThreadIndex
{
    private const string Msg = "This property can only be executed on the GPU.";
    public static int X => throw new InvalidOperationException(Msg);
    public static int Y => throw new InvalidOperationException(Msg);
    public static int Z => throw new InvalidOperationException(Msg);
}

public static class BlockIndex
{
    private const string Msg = "This property can only be executed on the GPU.";
    public static int X => throw new InvalidOperationException(Msg);
    public static int Y => throw new InvalidOperationException(Msg);
    public static int Z => throw new InvalidOperationException(Msg);
}

public static class BlockDimension
{
    private const string Msg = "This property can only be executed on the GPU.";
    public static int X => throw new InvalidOperationException(Msg);
    public static int Y => throw new InvalidOperationException(Msg);
    public static int Z => throw new InvalidOperationException(Msg);
}

public static class GridDimension
{
    private const string Msg = "This property can only be executed on the GPU.";
    public static int X => throw new InvalidOperationException(Msg);
    public static int Y => throw new InvalidOperationException(Msg);
    public static int Z => throw new InvalidOperationException(Msg);
}

public static class Gpu
{
    private const string Msg = "This method can only be executed on the GPU.";

    public static void SyncThreads() => throw new InvalidOperationException(Msg);

    public static int AtomicAdd(ref int address, int val) => throw new InvalidOperationException(Msg);
    public static float AtomicAdd(ref float address, float val) => throw new InvalidOperationException(Msg);
}
