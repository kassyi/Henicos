namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Inputs shared across one run of <see cref="IrDecisions"/>.</summary>
/// <param name="compilation">The compilation containing the method or state machine.</param>
/// <param name="temporaries">The temporary variable initializers of the tree being decided.</param>
internal sealed class IrDecisionContext(Compilation compilation, IrTemporaryInitializers temporaries)
{
    /// <summary>The compilation containing the method or state machine.</summary>
    public Compilation Compilation { get; } = compilation;

    /// <summary>The temporary variable initializers of the tree being decided.</summary>
    public IrTemporaryInitializers TemporaryInitializers { get; } = temporaries;
}
