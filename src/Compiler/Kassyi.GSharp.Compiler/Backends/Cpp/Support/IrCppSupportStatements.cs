using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>The rows of the C++ writer for the statements.</summary>
internal static class IrCppSupportStatements
{
    /// <summary>
    /// Whether <paramref name="node"/> is a statement and, when it is, whether the C++ writer spells it on
    /// <paramref name="profile"/>; the children are counted on their own.
    /// </summary>
    public static bool TryOf(IrNode node, IrNode? parent, CppBodyProfile profile, out IrSupport support)
    {
        IrSupport? row = node switch
        {
            IrBlock => IrSupport.Spelled("Block"),
            IrIf => IrSupport.Spelled("If"),
            IrWhile => IrSupport.Spelled("While"),
            IrExpressionStatement => IrSupport.Spelled("ExpressionStatement"),
            IrReturn => IrSupport.Spelled("Return"),
            IrLocalDeclaration declaration => OfDeclaration(declaration),
            // The scan stops a body with an opaque node before the check
            IrOpaqueStatement => IrSupport.Unspelled("Opaque"),
            _ => null
        };
        support = row.GetValueOrDefault();
        return row.HasValue;
    }

    private static IrSupport OfDeclaration(IrLocalDeclaration declaration)
    {
        var variable = declaration.Variable;
        if (variable.RefKind != RefKind.None)
        {
            return IrSupport.Unspelled("Ref");
        }

        if (variable.Symbol is ILocalSymbol { IsConst: true })
        {
            return IrSupport.Unspelled("Const");
        }

        if (IrCppSupportTypes.UnspellableShape(variable.Type) is { } shape)
        {
            return IrCppSupportTypes.Unspelled(shape);
        }

        return IrSupport.Spelled(declaration.DeducesType ? "Deduced" : "Named");
    }
}
