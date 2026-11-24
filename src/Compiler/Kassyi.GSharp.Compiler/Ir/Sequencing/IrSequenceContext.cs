using Kassyi.GSharp.Compiler.Ir.Decisions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>The inputs of one run of <see cref="IrSequencer"/>.</summary>
/// <param name="decisions">The context the decisions of the tree were made with.</param>
internal sealed class IrSequenceContext(IrDecisionContext decisions)
{
    /// <summary>The context the decisions of the tree were made with.</summary>
    public IrDecisionContext Decisions { get; } = decisions;

    /// <summary>The compilation of the method.</summary>
    public Compilation Compilation => Decisions.Compilation;
}
