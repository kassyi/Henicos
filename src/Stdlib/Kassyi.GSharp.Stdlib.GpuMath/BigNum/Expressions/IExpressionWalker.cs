namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// What the post-order walk of <see cref="ExpressionWalk"/> asks of the expression tree it evaluates: the signed tree
/// (<see cref="SignedTree"/>) and the unsigned one (<see cref="BigIntExpressionVisitor"/>).
/// </summary>
/// <typeparam name="TNode">The node type of the tree.</typeparam>
/// <typeparam name="TValue">The value of an evaluated node.</typeparam>
/// <typeparam name="TTarget">What a node is evaluated into.</typeparam>
internal interface IExpressionWalker<TNode, TValue, TTarget>
    where TNode : class
    where TValue : struct
{
    /// <summary>Gets the operand count of a node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>1 or 2 for an operator node; 0 for a node that evaluates itself (a leaf, or a node implemented
    /// outside GpuMath).</returns>
    int OperandCount(TNode node);

    /// <summary>Gets an operand of an operator node.</summary>
    /// <param name="node">The operator node.</param>
    /// <param name="index">0 for the first operand, 1 for the second.</param>
    /// <returns>The operand.</returns>
    TNode GetOperand(TNode node, int index);

    /// <summary>Gets the target the operands of a node are evaluated into.</summary>
    /// <param name="node">The operator node.</param>
    /// <param name="target">The target of the node.</param>
    /// <returns>The target of its operands.</returns>
    TTarget OperandTarget(TNode node, TTarget target);

    /// <summary>Called for each node before any of its operands is evaluated; a node rejects a target it cannot use
    /// here.</summary>
    /// <param name="node">The node.</param>
    /// <param name="target">Its target.</param>
    void Enter(TNode node, TTarget target);

    /// <summary>Evaluates a node without operands, which evaluates itself.</summary>
    /// <param name="node">The node.</param>
    /// <param name="target">Its target.</param>
    /// <returns>Its value.</returns>
    TValue EvaluateLeaf(TNode node, TTarget target);

    /// <summary>Applies an operator node to its evaluated operands. The call owns the operands and releases them, also
    /// when it fails.</summary>
    /// <param name="node">The operator node.</param>
    /// <param name="first">The value of the first operand.</param>
    /// <param name="second">The value of the second operand; the default value for a unary node.</param>
    /// <param name="target">The target of the node.</param>
    /// <returns>The value of the node.</returns>
    TValue Apply(TNode node, TValue first, TValue second, TTarget target);

    /// <summary>Releases a value that no node will use (the walk failed).</summary>
    /// <param name="value">The value.</param>
    void Release(TValue value);
}
