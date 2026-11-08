namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The cases of <see cref="ExpressionEffects.Walker.AddLocation"/>: each adds the reads that find a location (indices,
/// receivers) and returns the variable the location belongs to, without reading or writing the location itself.
/// </summary>
internal static class LocationEffects
{
    public static ExpressionEffects.Root AddConditional(
        ref ExpressionEffects.Walker walker,
        ConditionalExpressionSyntax conditional
    )
    {
        walker.AddValue(conditional.Condition);
        var whenTrue = walker.AddLocation(conditional.WhenTrue);
        var whenFalse = walker.AddLocation(conditional.WhenFalse);
        return ExpressionEffects.Root.Combine(whenTrue, whenFalse);
    }

    public static ExpressionEffects.Root AddDeclaration(
        ref ExpressionEffects.Walker walker,
        SingleVariableDesignationSyntax designation
    ) =>
        walker.Model.GetDeclaredSymbol<ILocalSymbol>(designation) is { } declared
            ? RootOfVariable(ref walker, declared)
            : new ExpressionEffects.Root(designation.Identifier.ValueText);

    public static ExpressionEffects.Root AddIdentifier(
        ref ExpressionEffects.Walker walker,
        IdentifierNameSyntax identifier
    ) =>
        walker.Model.GetSymbol(identifier) is { } symbol
            ? RootOfSymbol(ref walker, symbol)
            : new ExpressionEffects.Root(identifier.Identifier.ValueText);

    // A static member is memory; a field of a struct is part of the struct; a member of anything else is memory
    public static ExpressionEffects.Root AddMember(
        ref ExpressionEffects.Walker walker,
        MemberAccessExpressionSyntax access
    )
    {
        var model = walker.Model;
        var member = model.GetSymbol(access) ?? model.GetSymbol(access.Name);
        if (member is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
        {
            return new ExpressionEffects.Root(null);
        }

        if (model.GetTypeSymbol(access.Expression) is { IsValueType: true } &&
            access.Expression is not ThisExpressionSyntax)
        {
            return walker.AddLocation(access.Expression);
        }

        walker.AddValue(access.Expression);
        return new ExpressionEffects.Root(null);
    }

    // An element of a struct (an inline array) is part of the struct; any other element is memory
    public static ExpressionEffects.Root AddElement(
        ref ExpressionEffects.Walker walker,
        ElementAccessExpressionSyntax element
    )
    {
        if (walker.Model.GetTypeSymbol(element.Expression) is { IsValueType: true, IsRefLikeType: false }
                and not IPointerTypeSymbol &&
            element.Expression is not ThisExpressionSyntax)
        {
            var receiver = walker.AddLocation(element.Expression);
            walker.AddArgumentValues(element.ArgumentList.Arguments);
            return receiver;
        }

        walker.AddValue(element.Expression);
        walker.AddArgumentValues(element.ArgumentList.Arguments);
        return new ExpressionEffects.Root(null);
    }

    public static ExpressionEffects.Root AddIndirection(
        ref ExpressionEffects.Walker walker,
        PrefixUnaryExpressionSyntax indirection
    )
    {
        walker.AddValue(indirection.Operand);
        return new ExpressionEffects.Root(null);
    }

    // Any other expression: a value, whose location is memory
    public static ExpressionEffects.Root AddValue(ref ExpressionEffects.Walker walker, ExpressionSyntax expression)
    {
        walker.AddValue(expression);
        return new ExpressionEffects.Root(null);
    }

    // A non-ref local or by-value parameter as itself (RootOfVariable), anything else as memory
    public static ExpressionEffects.Root RootOfSymbol(ref ExpressionEffects.Walker walker, ISymbol symbol) =>
        symbol switch
        {
            ILocalSymbol { IsRef: false } local => RootOfVariable(ref walker, local),
            IParameterSymbol { RefKind: RefKind.None } parameter => RootOfVariable(ref walker, parameter),
            _ => new ExpressionEffects.Root(null)
        };

    // A local or by-value parameter, or memory when it has another name (AliasedVariables)
    private static ExpressionEffects.Root RootOfVariable(ref ExpressionEffects.Walker walker, ISymbol variable) =>
        AliasedVariables.Contains(variable, walker.Model.Compilation)
            ? new ExpressionEffects.Root(null)
            : new ExpressionEffects.Root(variable);
}
