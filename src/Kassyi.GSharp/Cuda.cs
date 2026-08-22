namespace Kassyi.GSharp;

/// <summary>
/// Provides CUDA intrinsic functions and variables.
/// These methods have no implementation in C# and are only meant to be compiled into GPU kernels by the G# Transpiler.
/// </summary>
public static class Cuda
{
    // ---------------------------------------------------------
    // Thread and Block Indices
    // ---------------------------------------------------------
    private const string InvalidOperationMessage = "This property can only be executed on the GPU.";

    public static int ThreadIdxX =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int ThreadIdxY =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int ThreadIdxZ =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockIdxX =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockIdxY =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockIdxZ =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockDimX =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockDimY =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int BlockDimZ =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int GridDimX =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int GridDimY =>
        throw new InvalidOperationException(InvalidOperationMessage);

    public static int GridDimZ =>
        throw new InvalidOperationException(InvalidOperationMessage);

    // ---------------------------------------------------------
    // Synchronization
    // ---------------------------------------------------------

    public static void SyncThreads() =>
        throw new InvalidOperationException("This method can only be executed on the GPU.");

    // ---------------------------------------------------------
    // Atomics
    // ---------------------------------------------------------

    public static int AtomicAdd(ref int address, int val) =>
        throw new InvalidOperationException("This method can only be executed on the GPU.");

    public static float AtomicAdd(ref float address, float val) =>
        throw new InvalidOperationException("This method can only be executed on the GPU.");
}
