namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Common execution state for iterative Newton-Raphson and NTT-based MegaKernels.
/// </summary>
public struct NewtonRaphsonContext
{
    /// <summary>Input and output operand buffers.</summary>
    public OperandContext Operands;
    /// <summary>Dimension metadata for precision and word sizes.</summary>
    public DimensionContext Dimensions;
    /// <summary>NTT transformation context.</summary>
    public Ntt Ntt;
    /// <summary>State scratch buffers for intermediate iterations.</summary>
    public WorkspaceStateContext Workspace;
    /// <summary>Carry accumulation buffer.</summary>
    public long[]? CarryBuffer;
    /// <summary>
    /// The workspace's correction status words: the final correction adds 1 to the failure count in word 0 and writes
    /// its code to word 1 when it does not converge, and the host reports a new count at the next read of the stream.
    /// </summary>
    public uint[]? CorrectionStatus;
}
