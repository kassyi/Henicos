namespace Kassyi.GSharp.Runtime.Core;

/// <summary>
/// Dummy structs to allow intellisense for CUDA built-in variables in C#.
/// The Transpiler will intercept these and replace them with CUDA equivalents.
/// </summary>

public struct Dim3
{
    public int X => 0;
    public int Y => 0;
    public int Z => 0;
}

public static class ThreadIdx
{
    public static int X => 0;
    public static int Y => 0;
    public static int Z => 0;
}

public static class BlockIdx
{
    public static int X => 0;
    public static int Y => 0;
    public static int Z => 0;
}

public static class BlockDim
{
    public static int X => 0;
    public static int Y => 0;
    public static int Z => 0;
}

public static class GridDim
{
    public static int X => 0;
    public static int Y => 0;
    public static int Z => 0;
}

public static class Gpu
{
    public static void SyncThreads() { }
    
    // Using ref for atomics so it modifies the original variable
    public static int AtomicAdd(ref int address, int val) => 0;
    public static float AtomicAdd(ref float address, float val) => 0f;
    
    // Add more intrinsics as needed (e.g. __popc)
}
