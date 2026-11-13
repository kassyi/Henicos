namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The types that the launchers carry through a native shadow struct in <c>AotContexts</c> with <c>Marshal_</c> and
/// <c>Unmarshal_</c> methods: a user struct, and a <c>Nullable&lt;T&gt;</c> of a value with a device representation.
/// </summary>
/// <remarks>
/// Every host emitter asks this class, not <see cref="TypeLayoutCalculator.IsUserStruct"/>, whether a type has a shadow
/// and whether a dialect converts it, so that a <c>Nullable&lt;T&gt;</c> takes the same paths as a user struct (#961).
/// </remarks>
internal static class HostShadowStructs
{
    /// <summary>Whether <paramref name="type"/> has a native shadow struct.</summary>
    public static bool Has(ITypeSymbol type) =>
        TypeLayoutCalculator.IsUserStruct(type) || TryGetNullableValue(type, out _);

    /// <summary>
    /// The value type of a <c>Nullable&lt;T&gt;</c> that has a native shadow struct: <c>T</c> is not array-like and has
    /// a device representation under both dialects. A <c>Nullable&lt;T&gt;</c> of a type without one (<c>decimal?</c>)
    /// has no shadow; GSH0078 / GSH0079 report it.
    /// </summary>
    public static bool TryGetNullableValue(ITypeSymbol type, [NotNullWhen(true)] out ITypeSymbol? value)
    {
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying]
            } &&
            underlying is not ITypeParameterSymbol &&
            !underlying.IsArrayLikeType() &&
            StructLayoutValidator.HasDeviceRepresentation(underlying, HostDialects.Cuda) &&
            StructLayoutValidator.HasDeviceRepresentation(underlying, HostDialects.Cpu))
        {
            value = underlying;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> has a shadow and its host bytes are not its device bytes under
    /// <paramref name="dialect"/>, so that a launcher converts it member by member through <c>Marshal_</c>.
    /// </summary>
    public static bool IsConverted(ITypeSymbol type, ITranspilerDialect dialect)
    {
        if (TryGetNullableValue(type, out _))
        {
            // [WHY] The plan decides: an unsupported value has no plan to convert by and is reported instead
            return TransferPlanner.Plan(type, dialect) is TransferPlan.Nullable { IsIdentical: false };
        }

        return type is INamedTypeSymbol named &&
               TypeLayoutCalculator.IsUserStruct(named) &&
               TypeLayoutCalculator.GetStructLayout(named, dialect).Transfer != TransferClass.Identical;
    }

    /// <summary>
    /// Whether <paramref name="type"/> has a shadow and holds an array at any depth: a user struct with an array field
    /// (<see cref="TransferClassifier.TryFindArrayField"/>), or a <c>Nullable&lt;T&gt;</c> of one.
    /// </summary>
    public static bool HoldsArrayFields(ITypeSymbol type) =>
        Has(type) &&
        type.UnwrapNullable() is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TransferClassifier.TryFindArrayField(named, out _);

    /// <summary>
    /// Collects the shadowed types that <paramref name="type"/> reaches: the type itself, the members of a user struct
    /// (the element of an array-like member), and the value of a <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    public static void Collect(ITypeSymbol type, HashSet<INamedTypeSymbol> types)
    {
        if (TryGetNullableValue(type, out var value))
        {
            if (types.Add((INamedTypeSymbol)type))
            {
                Collect(value, types);
            }

            return;
        }

        if (!TypeLayoutCalculator.IsUserStruct(type) || !types.Add((INamedTypeSymbol)type))
        {
            return;
        }

        foreach (var member in StructAbiMembers.Get((INamedTypeSymbol)type))
        {
            var memberType = member.Type.TryGetArrayLikeElementType(out var element) ? element : member.Type;
            Collect(memberType, types);
        }
    }
}
