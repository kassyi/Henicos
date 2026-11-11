namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The effects of reading names, members and elements for <see cref="ExpressionEffects"/>: identifiers, properties,
/// pointer members, unresolved members and element accesses.
/// </summary>
internal static class MemberEffects
{
    public static void AddIdentifier(ref EffectWalker walker, IdentifierNameSyntax identifier)
    {
        switch (walker.Model.GetSymbol(identifier))
        {
            case null:
                // A temporary that lowering synthesized: known by its name
                walker.Reads.Add(identifier.Identifier.ValueText);
                return;
            case ILocalSymbol or IParameterSymbol or IFieldSymbol:
                walker.AddRead(walker.AddLocation(identifier));
                return;
            case IPropertySymbol { IsStatic: false } property:
                // A property of the implicit this
                AddGetterCall(ref walker, null, property);
                return;
            case IPropertySymbol property:
                AddPropertyRead(ref walker, property);
                return;
            default:
                // A method group, a type, a namespace
                return;
        }
    }

    // A property read by its name alone or through its type: memory unless it is an intrinsic
    public static void AddPropertyRead(ref EffectWalker walker, IPropertySymbol property)
    {
        if (!IsIntrinsicProperty(property))
        {
            walker.Reads.Memory = true;
        }
    }

    public static void AddPointerMember(ref EffectWalker walker, MemberAccessExpressionSyntax access)
    {
        walker.AddValue(access.Expression);
        walker.Reads.Memory = true;
    }

    public static void AddInstanceProperty(
        ref EffectWalker walker,
        MemberAccessExpressionSyntax access,
        IPropertySymbol property
    ) =>
        AddGetterCall(ref walker, access.Expression, property);

    // A member that does not resolve (a field that lowering synthesized): known by its name
    public static void AddUnresolvedMember(ref EffectWalker walker, MemberAccessExpressionSyntax access)
    {
        walker.AddValue(access.Expression);
        walker.Reads.Add(access.Name.Identifier.ValueText);
    }

    public static void AddElementAccess(ref EffectWalker walker, ElementAccessExpressionSyntax element)
    {
        if (walker.Model.GetSymbol(element) is IPropertySymbol { IsIndexer: true } indexer)
        {
            AddGetterCall(ref walker, element.Expression, indexer);
        }
        else
        {
            walker.AddValue(element.Expression);
        }

        walker.AddArgumentValues(element.ArgumentList.Arguments);
        walker.Reads.Memory = true;
    }

    // Reading a property or an indexer calls its getter on receiver (null: the implicit this, which is memory). A
    // getter that may change its struct reads and writes the receiver (#992); that getter and the getter of a computed
    // property may read anything, and write what DeviceFunctionEffects says.
    private static void AddGetterCall(ref EffectWalker walker, ExpressionSyntax? receiver, IPropertySymbol property)
    {
        var mutatesReceiver = MayMutateValueTypeReceiver(property, walker.Model.Compilation);
        if (mutatesReceiver)
        {
            var root = receiver != null ? walker.AddReceiver(receiver) : new EffectRoot(null);
            walker.AddRead(root);
            walker.AddWrite(root);
        }
        else if (receiver != null)
        {
            walker.AddValue(receiver);
        }
        else
        {
            AddPropertyRead(ref walker, property);
        }

        if (!mutatesReceiver && !IsUserAccessorProperty(property))
        {
            return;
        }

        walker.AddUserCode(property.GetMethod);
    }

    /// <summary>
    /// Whether reading <paramref name="expression"/> calls a getter, so that the location of its result is memory (a
    /// temporary, or wherever a ref-returning getter points): a computed property (<c>s.P</c>, or <c>P</c> through the
    /// implicit <c>this</c>), or an indexer that returns by value (an element of an <c>ImmutableArray&lt;T&gt;</c> or
    /// an <c>ArraySegment&lt;T&gt;</c> lives in memory, not in the struct).
    /// </summary>
    public static bool IsGetterCall(ExpressionSyntax expression, SemanticModel model)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax &&
               model.GetSymbol(expression) is IPropertySymbol property &&
               (IsUserAccessorProperty(property) || property is { IsIndexer: true, RefKind: RefKind.None });
    }

    // __gsharp_ctx-><local>: ContextHoister moves a local (or a lowering temporary) of a state machine into its frame,
    // and the member keeps the span of the local
    public static bool IsFrameAccess(MemberAccessExpressionSyntax access) =>
        access.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.StateMachine.Context };

    // A computed property (with accessor bodies) of a user struct that G# defines on the device
    public static bool IsUserAccessorProperty(IPropertySymbol property) =>
        property.ContainingType is { } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        StructAbiMembers.HasAccessorFunctions(property);

    // ThreadIndex.X, BlockDim.Y ...: a value that no expression of the thread changes
    public static bool IsIntrinsicProperty(IPropertySymbol property) =>
        property.HasAttribute(KnownAttributes.CudaIntrinsic) ||
        property.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true;

    // A non-readonly getter of a non-readonly struct receives its receiver by reference and may mutate it (#992)
    public static bool MayMutateValueTypeReceiver(IPropertySymbol property, Compilation compilation) =>
        !property.IsStatic &&
        property.ContainingType is { } type &&
        GlobalCppNames.IsEmittedUserType(type) &&
        !type.IsReadOnly &&
        (StructAbiMembers.HasAccessorFunctions(property) || property.IsIndexer) &&
        property.GetMethod is { IsReadOnly: false } getter &&
        DeviceFunctionEffects.Classify(getter, compilation) != DeviceFunctionEffect.None;
}
