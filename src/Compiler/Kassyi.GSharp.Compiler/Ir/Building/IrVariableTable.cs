namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// The locals and parameters of one build: one <see cref="IrVariable"/> per symbol and region, so that every reference
/// to a variable holds the same instance.
/// </summary>
/// <param name="context">The build the variables belong to.</param>
internal sealed class IrVariableTable(IrBuildContext context)
{
    private readonly Dictionary<ISymbol, IrVariable> _variables = new(SymbolEqualityComparer.Default);

    /// <summary>The variable of a local.</summary>
    public IrVariable GetLocal(ILocalSymbol local)
    {
        if (!_variables.TryGetValue(local, out var variable))
        {
            variable = IrVariable.CreateLocal(local, context.Region, DeclarationSourceOf(local));
            _variables.Add(local, variable);
        }

        return variable;
    }

    /// <summary>The variable of a parameter.</summary>
    public IrVariable GetParameter(IParameterSymbol parameter)
    {
        if (!_variables.TryGetValue(parameter, out var variable))
        {
            variable = IrVariable.CreateParameter(parameter, context.Region, DeclarationSourceOf(parameter));
            _variables.Add(parameter, variable);
        }

        return variable;
    }

    // A symbol declared in the user's source has its declaration; one the compiler declared is placed at the method
    private IrSource DeclarationSourceOf(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences is [var reference, ..]
            ? context.SourceOf(reference.GetSyntax(context.CancellationToken), synthesized: false)
            : context.DeclarationSource.AsSynthesized();
}
