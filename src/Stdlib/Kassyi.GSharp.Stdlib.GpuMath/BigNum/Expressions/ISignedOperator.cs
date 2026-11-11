namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A signed operator node: its operands are evaluated by <see cref="SignedTree"/>, which walks the tree without
/// recursion.
/// </summary>
internal interface ISignedOperator : IGpuBigIntExpression
{
    /// <summary>Gets the number of operands, 1 or 2.</summary>
    int OperandCount { get; }

    /// <summary>Gets an operand.</summary>
    /// <param name="index">0 for the first operand, 1 for the second.</param>
    /// <returns>The operand.</returns>
    IGpuBigIntExpression GetOperand(int index);

    /// <summary>Gets a value indicating whether the operand is evaluated into the node's own target (a negation only
    /// flips the sign of its operand).</summary>
    bool PassesTargetToOperand { get; }

    /// <summary>Gets the operation, for the sign planner (<see cref="SignPlanner"/>).</summary>
    SignedOperation Operation { get; }

    /// <summary>
    /// Applies the node to its evaluated operands, which the call owns and releases, also when it fails. The target,
    /// when given, is at least <see cref="IGpuBigIntExpression.NumWords"/> wide (checked before the operands were
    /// evaluated).
    /// </summary>
    /// <param name="workspace">The workspace of the evaluation.</param>
    /// <param name="first">The value of the first operand.</param>
    /// <param name="second">The value of the second operand; the default value for a unary node.</param>
    /// <param name="targetResult">The target, or <see langword="null"/> for a rented result.</param>
    /// <returns>The value of the node.</returns>
    SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult);
}
