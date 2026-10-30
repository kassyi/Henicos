using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Decides, once per type and dialect, how a value crosses the host-device boundary (<see cref="TransferPlan"/>).
/// </summary>
/// <remarks>
/// The sizes and offsets come from <see cref="TypeLayoutCalculator"/>; the planner only decides the kind. No emitter
/// reads the plan yet (#976); the launch emitters and the layout diagnostics move onto it in later steps.
/// </remarks>
internal static class TransferPlanner
{
    /// <summary>The transfer plan of <paramref name="type"/> under <paramref name="dialect"/>.</summary>
    /// <remarks>
    /// <paramref name="type"/> is a constructed type, as a kernel parameter or return type is. An unbound type
    /// parameter has no layout and is planned as <see cref="TransferPlan.Unsupported"/>, although
    /// <see cref="TypeLayoutCalculator.HasDeviceRepresentation"/> accepts it as a struct member.
    /// </remarks>
    public static TransferPlan Plan(ITypeSymbol type, ITranspilerDialect dialect) =>
        PlanType(type, dialect, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static TransferPlan PlanType(ITypeSymbol type, ITranspilerDialect dialect, HashSet<ITypeSymbol> inProgress)
    {
        type = type.UnwrapNullableArrayLike() ?? type;

        if (type.TryGetArrayLikeElementType(out var element))
        {
            return new TransferPlan.ArrayView(PlanType(element, dialect, inProgress), dialect.ArrayViewLayout.Size);
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
            type.TypeKind == TypeKind.Enum ||
            dialect.TryGetTypeLayout(type, out _))
        {
            return PlanLeaf(type, dialect);
        }

        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            return PlanNullable(nullableType, dialect, inProgress);
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } structType)
        {
            // [WHY] A struct cannot contain itself by value (CS0523), only through an array; the plan is a tree, so the
            // inner occurrence refers back to the enclosing plan by name
            if (!inProgress.Add(structType))
            {
                return new TransferPlan.StructRef(
                    structType.GetFullyQualifiedCsharpName(),
                    AotHostNames.GetAotStructPath(structType),
                    TypeLayoutCalculator.TryGetHostLayout(structType, out var host) ? host.Size : null,
                    TypeLayoutCalculator.GetDeviceLayout(structType, dialect).Size
                );
            }

            var plan = PlanStruct(structType, dialect, inProgress);
            inProgress.Remove(structType);
            return plan;
        }

        return Unsupported($"'{TypeLayoutCalculator.FormatTypeName(type)}' has no layout on the device");
    }

    // A primitive, an enum, a pointer or a type of the dialect's type map: one block when both sides have its size
    private static TransferPlan PlanLeaf(ITypeSymbol type, ITranspilerDialect dialect)
    {
        var device = TypeLayoutCalculator.GetDeviceLayout(type, dialect);
        if (!TypeLayoutCalculator.TryGetHostLayout(type, out var host))
        {
            return Unsupported($"the host layout of '{TypeLayoutCalculator.FormatTypeName(type)}' is not known");
        }

        // [WHY] No entry of the dialect tables has a size other than the host's today. A leaf has no shadow struct
        // to convert through, so an entry that did would have to be reported rather than copied with the wrong size
        if (host.Size != device.Size)
        {
            return Unsupported($"'{TypeLayoutCalculator.FormatTypeName(type)}' is {host.Size} bytes on the host " +
                               $"and {device.Size} bytes on the device");
        }

        return new TransferPlan.Blit(
            host.Size,
            type.GetFullyQualifiedCsharpName(),
            null,
            ImmutableArray<MemberPlan>.Empty.AsEquatableArray()
        );
    }

    private static TransferPlan PlanNullable(INamedTypeSymbol nullableType, ITranspilerDialect dialect,
        HashSet<ITypeSymbol> inProgress)
    {
        var valueType = nullableType.TypeArguments[0];
        var value = PlanType(valueType, dialect, inProgress);
        if (value is TransferPlan.Unsupported)
        {
            return value;
        }

        // [WHY] A value whose host layout the CLR decides at run time (a reference field) leaves no host layout for the
        // Nullable around it either
        if (!TypeLayoutCalculator.TryGetHostLayout(nullableType, out var host))
        {
            return Unsupported($"the host layout of '{TypeLayoutCalculator.FormatTypeName(nullableType)}' is not known");
        }

        var device = TypeLayoutCalculator.GetDeviceLayout(nullableType, dialect);
        var valueOffset = TypeLayoutCalculator.AlignUp(1, TypeLayoutCalculator.GetDeviceLayout(valueType, dialect).Alignment);
        return new TransferPlan.Nullable(value, valueOffset, host.Size, device.Size);
    }

    private static TransferPlan PlanStruct(INamedTypeSymbol structType, ITranspilerDialect dialect,
        HashSet<ITypeSymbol> inProgress)
    {
        var isUserStruct = TypeLayoutCalculator.IsUserStruct(structType);
        if (!isUserStruct && !TypeLayoutCalculator.HasDeviceRepresentation(structType, dialect))
        {
            return Unsupported($"'{TypeLayoutCalculator.FormatTypeName(structType)}' has no device representation");
        }

        var layout = TypeLayoutCalculator.GetStructLayout(structType, dialect);
        if (layout.Unsupported is { } unsupported)
        {
            return unsupported is { IsMissingDeviceType: true, FieldType: { } fieldType }
                ? new TransferPlan.Unsupported(
                    GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id,
                    unsupported.Reason,
                    unsupported.FieldPath,
                    TypeLayoutCalculator.FormatTypeName(fieldType)
                )
                : Unsupported(unsupported.Reason);
        }

        var members = PlanMembers(layout, dialect, inProgress);
        var csharpType = structType.GetFullyQualifiedCsharpName();

        if (layout.Transfer == TransferClass.Identical)
        {
            var displayName = isUserStruct ? GlobalCppNames.GetTypePath(structType).ToDisplayString() : null;
            return new TransferPlan.Blit(layout.Host.Size, csharpType, displayName, members);
        }

        // [WHY] Only a user struct has a native shadow struct to convert through
        if (!isUserStruct)
        {
            return Unsupported(TypeLayoutCalculator.DescribeLayoutDifference(structType, dialect));
        }

        return new TransferPlan.Convert(
            csharpType,
            AotHostNames.GetAotStructPath(structType),
            members,
            layout.Host.IsKnown ? layout.Host.Size : null,
            layout.Device.Size
        );
    }

    // [WHY] An index loop: the host and device fields are two arrays in the same member order
    private static EquatableArray<MemberPlan> PlanMembers(StructLayoutInfo layout, ITranspilerDialect dialect,
        HashSet<ITypeSymbol> inProgress)
    {
        var members = ImmutableArray.CreateBuilder<MemberPlan>(layout.DeviceFields.Length);
        for (var i = 0; i < layout.DeviceFields.Length; i++)
        {
            var field = layout.DeviceFields[i];
            var member = field.Member;
            var access = GetAccess(member, out var accessAssembly);
            members.Add(new MemberPlan(
                member.Name,
                layout.Host.IsKnown ? layout.Host.Fields[i].Offset : null,
                field.Offset,
                member.ElementCount,
                member.Type.GetFullyQualifiedCsharpName(),
                access,
                accessAssembly,
                PlanType(member.Type, dialect, inProgress)
            ));
        }

        return members.MoveToImmutable().AsEquatableArray();
    }

    // An ordinary field that a launcher can name: public, or internal in its own assembly. A pointer cannot be a type
    // argument of Unsafe.As, so a pointer field is left to the size check (KernelHostModel.IsNameableField).
    private static MemberAccess GetAccess(StructAbiMember member, out string accessAssembly)
    {
        accessAssembly = string.Empty;
        if (member is not { Kind: StructAbiMemberKind.Field, Symbol: IFieldSymbol field } ||
            field.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            return MemberAccess.NotNameable;
        }

        switch (field.DeclaredAccessibility)
        {
            case Accessibility.Public:
                return MemberAccess.Public;
            case Accessibility.Internal or Accessibility.ProtectedOrInternal:
                accessAssembly = field.ContainingAssembly.Name;
                return MemberAccess.Internal;
            default:
                return MemberAccess.NotNameable;
        }
    }

    private static TransferPlan.Unsupported Unsupported(string reason) =>
        new(GSharpDiagnostics.StructLayoutNotRepresentable.Id, reason, null, null);
}
