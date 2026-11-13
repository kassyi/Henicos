namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates input operands, constant parameters, and destination buffers for arithmetic kernels.
/// </summary>
public struct OperandContext
{
    /// <summary>Left input operand buffer.</summary>
    [GpuReadOnly] public uint[]? LeftOperand;
    /// <summary>Right input operand buffer.</summary>
    [GpuReadOnly] public uint[]? RightOperand;
    /// <summary>Target operand buffer for in-place or unary transformations.</summary>
    public uint[] TargetOperand;
    /// <summary>Destination output result buffer.</summary>
    public uint[] ResultBuffer;
    /// <summary>General constant operand buffer.</summary>
    public uint[]? Constant;
    /// <summary>Specialized constant 3 operand buffer for Newton-Raphson iterations.</summary>
    public uint[]? Constant3;
    /// <summary>Carry accumulation buffer for multi-precision addition and subtraction.</summary>
    public long[]? CarryBuffer;
}