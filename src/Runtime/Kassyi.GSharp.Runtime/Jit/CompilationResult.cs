namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Encapsulates static compilation output metrics parsed from the CUDA compiler backend (NVCC / PTXAS).
/// </summary>
public class CompilationResult
{
    /// <summary>
    /// Gets or sets the count of hardware registers used per thread by the compiled GPU kernel.
    /// </summary>
    public int RegistersUsed { get; set; }

    /// <summary>
    /// Gets or sets the static block-shared memory consumed by the compiled GPU kernel, in bytes.
    /// </summary>
    public int SharedMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the constant memory consumed by the compiled GPU kernel, in bytes.
    /// </summary>
    public int ConstantMemoryBytes { get; set; }
}
