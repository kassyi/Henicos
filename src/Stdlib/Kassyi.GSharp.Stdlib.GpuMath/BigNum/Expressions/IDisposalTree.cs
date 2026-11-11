namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// What <see cref="ExpressionDisposal"/> asks of the expression tree it disposes: the signed tree
/// (<see cref="SignedTree"/>) and the vector expressions (<see cref="VectorWalk"/>).
/// </summary>
/// <typeparam name="TNode">The node type of the tree.</typeparam>
internal interface IDisposalTree<TNode>
    where TNode : class
{
    /// <summary>Releases what a node itself holds; a node without inputs is disposed whole.</summary>
    /// <param name="node">The node.</param>
    void Release(TNode node);

    /// <summary>Gets the number of inputs of a node that the tree disposes after the node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The number of inputs; 0 for a node disposed whole.</returns>
    int InputCount(TNode node);

    /// <summary>Gets an input of a node, in the order the inputs are disposed.</summary>
    /// <param name="node">The node.</param>
    /// <param name="index">0 for the first input.</param>
    /// <returns>The input.</returns>
    TNode GetInput(TNode node, int index);
}
