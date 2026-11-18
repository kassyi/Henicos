namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// What a node of a copy binds, converts to and equals, compared with the node of the declaration read as the
/// instance: the symbol (through the constraint where the declaration reaches it through a type parameter), the
/// conversion and the constant value.
/// </summary>
internal static class CopyBindings
{
    /// <summary>The reason the symbols differ, or null.</summary>
    public static string? CompareSymbols(CopyCheckContext context, SyntaxNode original, SyntaxNode copied)
    {
        var symbol = context.Model.GetSymbolInfo(original).Symbol;
        var copySymbol = context.CopyModel.GetSymbolInfo(copied).Symbol;
        // [WHY] Interface instance members accessed through a constrained type parameter resolve to the closed
        // type's exact interface implementation (FindImplementationForInterfaceMember), just like static members.
        if (symbol is not null &&
            (ConstrainedAccess.IsConstrainable(symbol) || ConstrainedAccess.IsInterfaceInstanceMember(symbol)) &&
            ConstrainedAccess.Find(original, context.Model, symbol) is ITypeParameterSymbol parameter &&
            context.Map.IsMapped(parameter))
        {
            return CompareConstrained(context, symbol, parameter, copySymbol);
        }

        var expected = context.Map.Substitute(symbol);
        return context.Comparer.Same(expected, copySymbol) ? null : Differs(copySymbol, expected);
    }

    /// <summary>The reason the conversions of the two nodes differ, or null.</summary>
    public static string? CompareConversions(CopyCheckContext context, SyntaxNode original, SyntaxNode copied)
    {
        if (original is not ExpressionSyntax expression || copied is not ExpressionSyntax copiedExpression)
        {
            return null;
        }

        var conversion = context.Model.GetConversion(expression);
        var copyConversion = context.CopyModel.GetConversion(copiedExpression);
        var same = conversion.Exists == copyConversion.Exists &&
                   conversion.IsImplicit == copyConversion.IsImplicit &&
                   conversion.IsNumeric == copyConversion.IsNumeric &&
                   conversion.IsNullable == copyConversion.IsNullable &&
                   conversion.IsUserDefined == copyConversion.IsUserDefined &&
                   context.Comparer.Same(context.Map.Substitute(conversion.MethodSymbol), copyConversion.MethodSymbol);
        return same ? null : "the copy converts the value another way";
    }

    /// <summary>The reason the constant values of the two nodes differ, or null.</summary>
    public static string? CompareConstants(CopyCheckContext context, SyntaxNode original, SyntaxNode copied)
    {
        var constant = context.Model.GetConstantValue(original);
        return !constant.HasValue || Equals(constant.Value, context.CopyModel.GetConstantValue(copied).Value)
            ? null
            : "the copy has another constant value";
    }

    private static string? CompareConstrained(
        CopyCheckContext context,
        ISymbol symbol,
        ITypeParameterSymbol parameter,
        ISymbol? copySymbol)
    {
        var resolved = context.Map.ResolveConstrained(symbol, parameter);
        var same = resolved switch
        {
            ConstrainedMember.Member member => context.Comparer.Same(member.Implementation, copySymbol),
            ConstrainedMember.BuiltinOperator builtin => copySymbol is IMethodSymbol
                                                         {
                                                             MethodKind: MethodKind.BuiltinOperator,
                                                         } op &&
                                                         SameOperator(op.Name, builtin.OperatorName),
            _ => false,
        };
        return same
            ? null
            : (resolved as ConstrainedMember.Unsupported)?.Reason ??
              Differs(copySymbol, (resolved as ConstrainedMember.Member)?.Implementation ?? symbol);
    }

    // The checked and unchecked operator of the same C# operator: the copy is in the same checked context
    private static bool SameOperator(string copyName, string? expectedName) =>
        expectedName is not null && CopySymbolComparer.SameOperatorName(copyName, expectedName);

    private static string Differs(ISymbol? copy, ISymbol? expected) =>
        $"the copy binds '{Describe(copy)}' where the declaration binds '{Describe(expected)}'";

    private static string Describe(ISymbol? symbol) =>
        symbol is null ? "nothing"
        : symbol.ContainingType is { } type ? type.Name + "." + symbol.Name
        : symbol.Name;
}
