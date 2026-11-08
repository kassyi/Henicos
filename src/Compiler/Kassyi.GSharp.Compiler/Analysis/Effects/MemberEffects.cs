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
    )
    {
        if (MayMutateValueTypeReceiver(property, walker.Model.Compilation))
        {
            var receiver = walker.AddLocation(access.Expression);
            walker.AddRead(receiver);
            walker.AddWrite(receiver);
        }
        else
        {
            walker.AddValue(access.Expression);
        }

        if (!IsComputedUserProperty(property))
        {
            return;
        }

        // The getter of a struct may read anything, and writes what DeviceFunctionEffects says
        walker.Reads.Memory = true;
        if (walker.MayWriteMemory(property.GetMethod))
        {
            walker.Writes.Memory = true;
        }
    }

    // A member that does not resolve (a field that lowering synthesized): known by its name
    public static void AddUnresolvedMember(ref EffectWalker walker, MemberAccessExpressionSyntax access)
    {
        walker.AddValue(access.Expression);
        walker.Reads.Add(access.Name.Identifier.ValueText);
    }

    public static void AddElementAccess(ref EffectWalker walker, ElementAccessExpressionSyntax element)
    {
        if (walker.Model.GetSymbol(element) is IPropertySymbol indexer &&
            MayMutateValueTypeReceiver(indexer, walker.Model.Compilation))
        {
            var receiver = walker.AddLocation(element.Expression);
            walker.AddRead(receiver);
            walker.AddWrite(receiver);
            if (walker.MayWriteMemory(indexer.GetMethod))
            {
                walker.Writes.Memory = true;
            }
        }
        else
        {
            walker.AddValue(element.Expression);
        }

        walker.AddArgumentValues(element.ArgumentList.Arguments);
        walker.Reads.Memory = true;
    }

    // __gsharp_ctx-><local>: ContextHoister moves a local (or a lowering temporary) of a state machine into its frame,
    // and the member keeps the span of the local
    public static bool IsFrameAccess(MemberAccessExpressionSyntax access) =>
        access.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.StateMachine.Context };

    // A computed property (with accessor bodies) of a user struct that G# defines on the device
    public static bool IsComputedUserProperty(IPropertySymbol property) =>
        property.ContainingType is { } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        StructAbiMembers.IsComputedProperty(property);

    // ThreadIndex.X, BlockDim.Y ...: a value that no expression of the thread changes
    private static bool IsIntrinsicProperty(IPropertySymbol property) =>
        property.HasAttribute(KnownAttributes.CudaIntrinsic) ||
        property.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true;

    // A non-readonly getter of a non-readonly struct receives its receiver by reference and may mutate it (#992)
    private static bool MayMutateValueTypeReceiver(IPropertySymbol property, Compilation compilation) =>
        !property.IsStatic &&
        property.ContainingType is { } type &&
        GlobalCppNames.IsEmittedUserType(type) &&
        !type.IsReadOnly &&
        (StructAbiMembers.IsComputedProperty(property) || property.IsIndexer) &&
        property.GetMethod is { IsReadOnly: false } getter &&
        DeviceFunctionEffects.Classify(getter, compilation) != DeviceFunctionEffect.None;
}
