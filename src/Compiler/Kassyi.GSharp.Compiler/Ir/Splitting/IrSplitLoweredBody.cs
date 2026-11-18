using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The result of <see cref="IrSplitLowering.Apply"/>: the body with every statement's split plan applied, and the
/// lowered loop conditions keyed by the rebuilt <see cref="IrWhile"/> instances.
/// </summary>
/// <param name="body">The rewritten method body.</param>
/// <param name="loopConditions">Lowered loop conditions keyed by the rebuilt <see cref="IrWhile"/> nodes.</param>
internal sealed class IrSplitLoweredBody(IrBlock body, ImmutableDictionary<IrWhile, IrLoweredCondition> loopConditions)
{
    /// <summary>The rewritten method body.</summary>
    public IrBlock Body { get; } = body;

    /// <summary>Lowered loop conditions keyed by the rebuilt <see cref="IrWhile"/> nodes.</summary>
    public ImmutableDictionary<IrWhile, IrLoweredCondition> LoopConditions { get; } = loopConditions;
}
