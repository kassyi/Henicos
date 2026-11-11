namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The signed operations, as the sign planner sees them
internal enum SignedOperation
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Negate,
    Not,
    Max,
    Min,
    Fit
}
