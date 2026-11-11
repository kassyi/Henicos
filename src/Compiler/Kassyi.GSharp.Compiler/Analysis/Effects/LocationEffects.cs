namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The cases of <see cref="EffectWalker.AddLocation"/>: each adds the reads that find a location (indices,
/// receivers) and returns the variable the location belongs to, without reading or writing the location itself.
/// </summary>
internal static class LocationEffects
{
    public static EffectRoot AddConditional(
        ref EffectWalker walker,
        ConditionalExpressionSyntax conditional
    )
    {
        walker.AddValue(conditional.Condition);
        var whenTrue = walker.AddLocation(conditional.WhenTrue);
        var whenFalse = walker.AddLocation(conditional.WhenFalse);
        return EffectRoot.Combine(whenTrue, whenFalse);
    }

    public static EffectRoot AddDeclaration(
        ref EffectWalker walker,
        SingleVariableDesignationSyntax designation
    ) =>
        walker.Model.GetDeclaredSymbol<ILocalSymbol>(designation) is { } declared
            ? RootOfVariable(ref walker, declared)
            : new EffectRoot(designation.Identifier.ValueText);

    public static EffectRoot AddIdentifier(
        ref EffectWalker walker,
        IdentifierNameSyntax identifier
    ) =>
        walker.Model.GetSymbol(identifier) is { } symbol
            ? RootOfSymbol(ref walker, symbol)
            : new EffectRoot(identifier.Identifier.ValueText);

    // A static member is memory; a field of a struct is part of the struct; a member of anything else is memory
    public static EffectRoot AddMember(
        ref EffectWalker walker,
        MemberAccessExpressionSyntax access
    )
    {
        var model = walker.Model;
        var member = model.GetSymbol(access) ?? model.GetSymbol(access.Name);
        if (member is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
        {
            return new EffectRoot(null);
        }

        if (model.GetTypeSymbol(access.Expression) is { IsValueType: true } &&
            access.Expression is not ThisExpressionSyntax)
        {
            return walker.AddReceiver(access.Expression);
        }

        walker.AddValue(access.Expression);
        return new EffectRoot(null);
    }

    // An element of a struct (an inline array) is part of the struct; any other element is memory
    public static EffectRoot AddElement(
        ref EffectWalker walker,
        ElementAccessExpressionSyntax element
    )
    {
        if (walker.Model.GetTypeSymbol(element.Expression) is { IsValueType: true, IsRefLikeType: false }
                and not IPointerTypeSymbol &&
            element.Expression is not ThisExpressionSyntax)
        {
            var receiver = walker.AddReceiver(element.Expression);
            walker.AddArgumentValues(element.ArgumentList.Arguments);
            return receiver;
        }

        walker.AddValue(element.Expression);
        walker.AddArgumentValues(element.ArgumentList.Arguments);
        return new EffectRoot(null);
    }

    public static EffectRoot AddIndirection(
        ref EffectWalker walker,
        PrefixUnaryExpressionSyntax indirection
    )
    {
        walker.AddValue(indirection.Operand);
        return new EffectRoot(null);
    }

    // Any other expression: a value, whose location is memory
    public static EffectRoot AddValue(ref EffectWalker walker, ExpressionSyntax expression)
    {
        walker.AddValue(expression);
        return new EffectRoot(null);
    }

    // A non-ref local or by-value parameter as itself (RootOfVariable), anything else as memory
    public static EffectRoot RootOfSymbol(ref EffectWalker walker, ISymbol symbol) =>
        symbol switch
        {
            ILocalSymbol { IsRef: false } local => RootOfVariable(ref walker, local),
            IParameterSymbol { RefKind: RefKind.None } parameter => RootOfVariable(ref walker, parameter),
            _ => new EffectRoot(null)
        };

    // A local or by-value parameter, or memory when it has another name (AliasedVariables)
    private static EffectRoot RootOfVariable(ref EffectWalker walker, ISymbol variable) =>
        AliasedVariables.Contains(variable, walker.Model.Compilation)
            ? new EffectRoot(null)
            : new EffectRoot(variable);
}
