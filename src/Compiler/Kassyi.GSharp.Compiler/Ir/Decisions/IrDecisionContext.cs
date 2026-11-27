namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Inputs shared across one run of <see cref="IrDecisions"/>.</summary>
/// <param name="compilation">The compilation containing the method.</param>
/// <param name="temporaries">The temporary variable initializers of the tree being decided.</param>
/// <param name="dialect">The dialect used for target, member, and argument decisions.</param>
/// <param name="original">The original pre-spill method body from which unwritten local initializers are taken.</param>
internal sealed class IrDecisionContext(
    Compilation compilation,
    IrTemporaryInitializers temporaries,
    ITranspilerDialect dialect,
    IrMethod original)
{
    /// <summary>The compilation containing the method.</summary>
    public Compilation Compilation { get; } = compilation;

    /// <summary>The temporary variable initializers of the tree being decided.</summary>
    public IrTemporaryInitializers TemporaryInitializers { get; } = temporaries;

    /// <summary>The dialect used for target, member, and argument decisions.</summary>
    public ITranspilerDialect Dialect { get; } = dialect;

    /// <summary>The method being decided.</summary>
    public IMethodSymbol Method { get; } = original.Symbol;

    /// <summary>The non-ref, non-const local initializers collected from the original pre-spill body.</summary>
    public IrLocalInitializers Initializers { get; } = IrLocalInitializers.Create(original);
}
