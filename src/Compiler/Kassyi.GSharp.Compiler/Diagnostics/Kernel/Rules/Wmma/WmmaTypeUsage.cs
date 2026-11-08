namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Wmma;

/// <summary>
/// Where a type that is, or contains, a WMMA fragment or marker may be used: fragments only as locals and device
/// function parameters, markers only as type arguments of fragments, and neither behind a pointer, in an array or
/// span, as a type argument or as a member of a user struct.
/// </summary>
internal static class WmmaTypeUsage
{
    private const int MaxDepth = 8;

    /// <summary>
    /// Whether <paramref name="type"/> may be used at this position; when it may not, the name of the offending type
    /// and the reason for the message.
    /// </summary>
    /// <param name="type">The type written at the position.</param>
    /// <param name="allowDirectFragment">
    /// Whether the position accepts a fragment itself (a local, a device function parameter).
    /// </param>
    /// <param name="isKernelParameter">Whether the position is a kernel parameter, which selects the message.</param>
    /// <param name="targetName">The name of the offending type.</param>
    /// <param name="reason">Why it cannot be used there.</param>
    public static bool IsAllowed(
        ITypeSymbol? type,
        bool allowDirectFragment,
        bool isKernelParameter,
        [NotNullWhen(false)] out string? targetName,
        [NotNullWhen(false)] out string? reason)
    {
        if (type is null)
        {
            targetName = null;
            reason = null;
            return true;
        }

        if (type.IsWmmaFragmentType() && type is INamedTypeSymbol fragmentType)
        {
            return IsAllowedFragment(fragmentType, allowDirectFragment, isKernelParameter, out targetName, out reason);
        }

        if (type.IsWmmaMarkerType())
        {
            targetName = type.Name;
            reason = "WMMA shape and layout markers can only be used as type arguments of WMMA fragments";
            return false;
        }

        return !TryFindNestedFragmentOrMarker(type, out targetName, out reason);
    }

    private static bool IsAllowedFragment(
        INamedTypeSymbol fragmentType,
        bool allowDirectFragment,
        bool isKernelParameter,
        [NotNullWhen(false)] out string? targetName,
        [NotNullWhen(false)] out string? reason)
    {
        if (!allowDirectFragment)
        {
            targetName = fragmentType.Name;
            reason = isKernelParameter
                ? "WMMA fragments cannot be passed as kernel parameters"
                : "WMMA fragments can only be declared as local variables or device function parameters";
            return false;
        }

        if (!WmmaFragmentTypes.IsSupported(fragmentType, out reason))
        {
            targetName = fragmentType.Name;
            return false;
        }

        targetName = null;
        return true;
    }

    // A fragment or marker behind a pointer, inside an array or span, among the type arguments, or in a struct member.
    private static bool TryFindNestedFragmentOrMarker(
        ITypeSymbol type,
        [NotNullWhen(true)] out string? targetName,
        [NotNullWhen(true)] out string? reason)
    {
        if (type is IPointerTypeSymbol pointerType &&
            ContainsFragmentOrMarker(pointerType.PointedAtType, out targetName, depth: 0))
        {
            reason = "pointers to WMMA fragments or markers are not supported";
            return true;
        }

        if (type.TryGetArrayLikeElementType(out var elementType) &&
            ContainsFragmentOrMarker(elementType, out targetName, depth: 0))
        {
            reason = "arrays or spans of WMMA fragments or markers are not supported";
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (TryFindInTypeArguments(namedType, depth: 0, out targetName))
            {
                reason = $"WMMA fragments and markers cannot be used as type arguments of '{namedType.Name}'";
                return true;
            }

            if (TryFindInStructMembers(namedType, depth: 1, out var member, out var memberWmmaName))
            {
                var memberKind = member is IFieldSymbol ? "field" : "property";
                targetName = namedType.Name;
                reason = $"struct '{namedType.Name}' cannot contain WMMA fragment or marker {memberKind} " +
                         $"'{member.Name}' of type '{memberWmmaName}'";
                return true;
            }
        }

        targetName = null;
        reason = null;
        return false;
    }

    private static bool ContainsFragmentOrMarker(
        ITypeSymbol? type,
        [NotNullWhen(true)] out string? wmmaTypeName,
        int depth)
    {
        if (type is null || depth > MaxDepth)
        {
            wmmaTypeName = null;
            return false;
        }

        if (type.IsWmmaFragmentType() || type.IsWmmaMarkerType())
        {
            wmmaTypeName = type.Name;
            return true;
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return ContainsFragmentOrMarker(pointerType.PointedAtType, out wmmaTypeName, depth + 1);
        }

        if (type.TryGetArrayLikeElementType(out var elementType))
        {
            return ContainsFragmentOrMarker(elementType, out wmmaTypeName, depth + 1);
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (TryFindInTypeArguments(namedType, depth + 1, out wmmaTypeName))
            {
                return true;
            }

            return TryFindInStructMembers(namedType, depth + 1, out _, out wmmaTypeName);
        }

        wmmaTypeName = null;
        return false;
    }

    private static bool TryFindInTypeArguments(
        INamedTypeSymbol type,
        int depth,
        [NotNullWhen(true)] out string? wmmaTypeName)
    {
        foreach (var typeArgument in type.TypeArguments)
        {
            if (ContainsFragmentOrMarker(typeArgument, out wmmaTypeName, depth))
            {
                return true;
            }
        }

        wmmaTypeName = null;
        return false;
    }

    // The instance fields and properties of a user struct (not System, System.Numerics or the G# core types).
    private static bool TryFindInStructMembers(
        INamedTypeSymbol type,
        int depth,
        [NotNullWhen(true)] out ISymbol? member,
        [NotNullWhen(true)] out string? wmmaTypeName)
    {
        if (type.TypeKind == TypeKind.Struct &&
            !type.ContainingNamespace.IsSystemOrNumerics() &&
            !type.ContainingNamespace.IsCoreNamespace())
        {
            foreach (var candidate in type.GetMembers())
            {
                if (GetInstanceMemberType(candidate) is { } memberType &&
                    ContainsFragmentOrMarker(memberType, out wmmaTypeName, depth))
                {
                    member = candidate;
                    return true;
                }
            }
        }

        member = null;
        wmmaTypeName = null;
        return false;
    }

    private static ITypeSymbol? GetInstanceMemberType(ISymbol member) =>
        member switch
        {
            IFieldSymbol { IsStatic: false } field => field.Type,
            IPropertySymbol { IsStatic: false } property => property.Type,
            _ => null
        };
}
