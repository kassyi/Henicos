using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Maps non-ref, non-const local variables in the original pre-spill <see cref="IrMethod"/> to their declaration
/// initializer expressions so <see cref="IrNonNullProof"/> inspects the original initializers (#1561).
/// </summary>
internal sealed class IrLocalInitializers
{
    private readonly Dictionary<IrVariable, IrExpression> _initializers;

    private IrLocalInitializers(Dictionary<IrVariable, IrExpression> initializers) =>
        _initializers = initializers;

    /// <summary>Collects the declaration initializers of local variables in <paramref name="original"/>.</summary>
    public static IrLocalInitializers Create(IrMethod original)
    {
        var initializers = new Dictionary<IrVariable, IrExpression>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(original.Body))
        {
            if (node is IrLocalDeclaration
                {
                    Variable:
                    {
                        Kind: IrVariableKind.Local,
                        RefKind: RefKind.None,
                        Symbol: ILocalSymbol { IsConst: false }
                    } variable,
                    Initializer: { } initializer
                })
            {
                initializers[variable] = initializer;
            }
        }

        return new IrLocalInitializers(initializers);
    }

    /// <summary>Gets the declaration initializer recorded for <paramref name="local"/>.</summary>
    public bool TryGet(IrVariable local, [NotNullWhen(true)] out IrExpression? initializer) =>
        _initializers.TryGetValue(local, out initializer);
}
