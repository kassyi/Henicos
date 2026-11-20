namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence pass of the native writer: moves the operands whose C# order of evaluation a C++ expression would not
/// keep into <see cref="Expressions.IrSequence"/> nodes, as the syntax path writes them into
/// <c>__gsharp_seq_&lt;n&gt;</c> temporaries (<see cref="EvaluationOrder"/>).
/// </summary>
/// <remarks>
/// The pass reads a decided tree and makes no decision of its own: which operands interfere is
/// <see cref="EvaluationOrder.LastSpilledOperand"/> over the effects of the original operands, and the new nodes are
/// of kinds that hold no decision. The managed writer reads the tree before this pass, since C# keeps the order itself.
/// </remarks>
internal static class IrSequencer
{
    /// <summary>
    /// The method body with the operands that C# evaluates in an order a C++ expression would not keep moved into
    /// sequences; <paramref name="method"/> itself when nothing needs one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tree has not been decided, or a call is not classified.
    /// </exception>
    public static IrMethod Sequence(IrMethod method, IrSequenceContext context) =>
        new IrSequenceRewriter(context).RewriteMethod(method);
}
