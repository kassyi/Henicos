namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A vector node of GpuMath with vector inputs, evaluated by <see cref="VectorWalk"/>: the node says which inputs it
/// reads and over which window, checks what it can before any input is evaluated, and computes its result from the
/// evaluated inputs. It never evaluates its inputs itself, so the depth of an expression does not reach the call stack.
/// </summary>
internal interface IVectorOperator
{
    /// <summary>Gets the number of vector inputs: 1 or 2.</summary>
    int OperandCount { get; }

    /// <summary>
    /// Gets a vector input (an <see cref="IGpuBigIntVectorExpression"/>, <see cref="IGpuBigFloatVectorExpression"/> or
    /// <see cref="IGpuBoolVectorExpression"/>), in the order the inputs are evaluated.
    /// </summary>
    /// <param name="index">0 for the first input, 1 for the second.</param>
    /// <returns>The input.</returns>
    object GetOperand(int index);

    /// <summary>Gets a vector input in the order <see cref="VectorWalk.Dispose"/> releases the inputs.</summary>
    /// <param name="index">0 for the first input released, 1 for the second.</param>
    /// <returns>The input.</returns>
    object GetInput(int index) => GetOperand(index);

    /// <summary>
    /// Gets whether evaluating the node can throw, by its own operation (a division, a square root, inputs of different
    /// lengths) or through an input (<see cref="VectorEffects"/>); fixed when the node is built.
    /// </summary>
    bool MayThrow { get; }

    /// <summary>
    /// Checks the request before any input is evaluated (a caller's target, the lengths of the inputs), so that a
    /// rejected request evaluates nothing.
    /// </summary>
    /// <param name="request">The request.</param>
    void Enter(in VectorRequest request)
    {
    }

    /// <summary>
    /// Gets whether the inputs are evaluated for <paramref name="request"/>; when they are not (an empty window), the
    /// node computes its result without them.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> to evaluate the inputs.</returns>
    bool EvaluatesOperands(in VectorRequest request) => true;

    /// <summary>Gets what the inputs are asked to evaluate; every input of a node gets the same request.</summary>
    /// <param name="request">The request of the node.</param>
    /// <returns>The request of its inputs.</returns>
    VectorRequest OperandRequest(in VectorRequest request);

    /// <summary>Computes the result of the node from the results of its inputs.</summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">The request of the node.</param>
    /// <param name="first">The result of the first input; the default value when the inputs were not evaluated.</param>
    /// <param name="second">The result of the second input; the default value for a node with one input.</param>
    /// <returns>The result of the node.</returns>
    VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second);

    /// <summary>
    /// Releases what the node itself holds (its result buffers); <see cref="VectorWalk.Dispose"/> releases its inputs.
    /// </summary>
    void DisposeOwn();

    /// <summary>
    /// Evaluates the node for <see cref="VectorWalk.EvaluateDirect"/>: checks the request, evaluates the inputs through
    /// <see cref="VectorWalk.EvaluateDirect"/> at <paramref name="level"/>, and applies the node, as one level of a
    /// recursive evaluation.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">The request of the node.</param>
    /// <param name="level">The level of the inputs of the node.</param>
    /// <returns>The result of the node.</returns>
    VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request, RecursionLevel level);
}
