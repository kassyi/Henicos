namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Represents the result of a JIT compilation pass, including parsed static analysis data from NVCC.
/// </summary>
public class CompilationResult
{
    /// <summary>
    /// The number of registers used per thread by the compiled kernel.
    /// </summary>
    public int RegistersUsed
    {
        get; set;
    }

    /// <summary>
    /// The amount of shared memory used per block by the compiled kernel (in bytes).
    /// </summary>
    public int SharedMemoryBytes
    {
        get; set;
    }

    /// <summary>
    /// The amount of constant memory used by the compiled kernel (in bytes).
    /// </summary>
    public int ConstantMemoryBytes
    {
        get; set;
    }
}
