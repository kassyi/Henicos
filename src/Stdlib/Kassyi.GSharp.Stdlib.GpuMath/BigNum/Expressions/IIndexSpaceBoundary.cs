namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Implemented by vector operators whose operands are evaluated in separate index spaces (such as <c>Where</c>, which
/// remaps operand exception indices into its output index space).
/// </summary>
internal interface IIndexSpaceBoundary
{
    /// <summary>Called before evaluating the <paramref name="index"/>-th operand.</summary>
    /// <param name="index">0 for the first operand, 1 for the second.</param>
    void BeginOperand(int index);
}
