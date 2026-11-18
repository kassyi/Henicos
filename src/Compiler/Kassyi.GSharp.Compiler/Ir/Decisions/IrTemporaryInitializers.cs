using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Maps compiler temporaries declared in a lowered IR tree to their initializer expressions so that range and
/// source-type queries read the original operand before spill lowering (#1561).
/// </summary>
internal sealed class IrTemporaryInitializers
{
    private readonly Dictionary<IrVariable, IrExpression> _initializers;

    private IrTemporaryInitializers(Dictionary<IrVariable, IrExpression> initializers) =>
        _initializers = initializers;

    /// <summary>Collects the temporary variable initializers under <paramref name="root"/>.</summary>
    public static IrTemporaryInitializers Create(IrNode root)
    {
        var initializers = new Dictionary<IrVariable, IrExpression>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            if (node is IrLocalDeclaration
                {
                    Variable: { Kind: IrVariableKind.Temporary } variable,
                    Initializer: { } initializer and not IrDefaultValue
                })
            {
                initializers[variable] = initializer;
            }
        }

        return new IrTemporaryInitializers(initializers);
    }

    /// <summary>
    /// Follows temporary variable references in <paramref name="expression"/> to the original initializer expression;
    /// returns <paramref name="expression"/> when it is not a temporary read or has no recorded initializer.
    /// </summary>
    public IrExpression Resolve(IrExpression expression)
    {
        HashSet<IrVariable>? seen = null;
        while (expression is IrVariableReference
               {
                   IsDeclaration: false,
                   Variable: { Kind: IrVariableKind.Temporary } variable
               } &&
               _initializers.TryGetValue(variable, out var initializer))
        {
            if (seen is not null)
            {
                if (!seen.Add(variable))
                {
                    break;
                }
            }
            else if (initializer is IrVariableReference
                     {
                         IsDeclaration: false,
                         Variable.Kind: IrVariableKind.Temporary
                     })
            {
                seen = [variable];
            }

            expression = initializer;
        }

        return expression;
    }
}
