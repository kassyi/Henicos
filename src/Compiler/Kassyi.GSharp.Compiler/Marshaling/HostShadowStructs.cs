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
    /// The value type of a <c>Nullable&lt;T&gt;</c> that has a native shadow struct: <c>T</c> has a device
    /// representation under both dialects, an array-like value (<c>Memory&lt;T&gt;?</c>, #1115) included. A
    /// <c>Nullable&lt;T&gt;</c> of a type without one (<c>decimal?</c>) has no shadow; GSH0078 / GSH0079 report it. A
    /// kernel parameter of a nullable array-like type keeps its own path (the nullable proxy of the launchers), since
    /// <see cref="KernelParameterShape.Classify"/> unwraps it first.
    /// </summary>
    public static bool TryGetNullableValue(ITypeSymbol type, [NotNullWhen(true)] out ITypeSymbol? value)
    {
        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying]
            } &&
            underlying is not ITypeParameterSymbol &&
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
    /// Whether the marshaling of a member, or of an element of an array member, under <paramref name="dialect"/> goes
    /// through the shadow of <paramref name="type"/>: every user struct (an identical one through <c>Unsafe.As</c>),
    /// and a <c>Nullable&lt;T&gt;</c> only when the dialect converts it; an identical <c>int?</c> keeps its bytes.
    /// </summary>
    public static bool UsesShadow(ITypeSymbol type, ITranspilerDialect dialect) =>
        TypeLayoutCalculator.IsUserStruct(type) || (TryGetNullableValue(type, out _) && IsConverted(type, dialect));

    /// <summary>
    /// Whether <paramref name="type"/> has a shadow and its host bytes are not its device bytes under
    /// <paramref name="dialect"/>, so that a launcher converts it member by member through <c>Marshal_</c>.
    /// </summary>
    public static bool IsConverted(ITypeSymbol type, ITranspilerDialect dialect)
    {
        if (TryGetNullableValue(type, out var value))
        {
            // [WHY] The plan decides: an unsupported value has no plan to convert by and is reported instead. An
            // array-like value is a reference on the host and an array view on the device, so it always converts
            return value.IsArrayLikeType() ||
                   TransferPlanner.Plan(type, dialect) is TransferPlan.Nullable { IsIdentical: false };
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
    /// (the element of an array-like member), and the value of a <c>Nullable&lt;T&gt;</c>. A <c>Nullable&lt;T&gt;</c>
    /// member is collected only when <paramref name="dialect"/> converts it (<see cref="UsesShadow"/>; either dialect
    /// when <see langword="null"/>), so that an identical one leaves no unused shadow.
    /// </summary>
    public static void Collect(ITypeSymbol type, HashSet<INamedTypeSymbol> types, ITranspilerDialect? dialect = null) =>
        Collect(type, types, dialect, isMember: false);

    private static void Collect(ITypeSymbol type, HashSet<INamedTypeSymbol> types, ITranspilerDialect? dialect,
        bool isMember)
    {
        if (TryGetNullableValue(type, out var value))
        {
            var used = !isMember ||
                       (dialect is null
                           ? IsConverted(type, HostDialects.Cuda) || IsConverted(type, HostDialects.Cpu)
                           : IsConverted(type, dialect));

            // The value of an identical Nullable member is copied with its bytes and needs no shadow of its own
            if (used && types.Add((INamedTypeSymbol)type))
            {
                var valueType = value.TryGetArrayLikeElementType(out var valueElement) ? valueElement : value;
                Collect(valueType, types, dialect, isMember: false);
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
            Collect(memberType, types, dialect, isMember: true);
        }
    }
}
