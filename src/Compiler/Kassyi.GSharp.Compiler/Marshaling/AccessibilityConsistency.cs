namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Roslyn-equivalent accessibility consistency checks (CS0050 to CS0053): whether every named type referenced by a
/// type (or a generated nested type) is at least as accessible as a member declared in a containing type.
/// </summary>
/// <remarks>
/// Mirrors <c>TypeSymbolExtensions.IsAtLeastAsVisibleAs</c>, <c>VisitType</c>, <c>IsAsRestrictive</c> and
/// <c>SymbolExtensions.IsAccessibleViaInheritance</c> from Roslyn using public symbol APIs.
/// </remarks>
internal static class AccessibilityConsistency
{
    /// <summary>
    /// Whether every constituent named type of <paramref name="type"/> is at least as visible as a member with
    /// <paramref name="memberAccessibility"/> declared in <paramref name="container"/> (or an <c>internal</c> member
    /// of a generated type with that accessibility inside <paramref name="container"/> when
    /// <paramref name="isInternalMember"/> is <see langword="true"/>).
    /// </summary>
    public static bool IsAtLeastAsVisibleAs(
        ITypeSymbol type,
        Accessibility memberAccessibility,
        INamedTypeSymbol container,
        bool isInternalMember = false) =>
        FindTypeLessVisibleThan(type, memberAccessibility, container, isInternalMember) is null;

    /// <summary>
    /// Whether a synthetic nested type declared in <paramref name="generatedContainer"/> with
    /// <paramref name="generatedAccessibility"/> is at least as visible as a member with
    /// <paramref name="memberAccessibility"/> declared in <paramref name="targetContainer"/> (or an <c>internal</c>
    /// member of a generated type inside <paramref name="targetContainer"/> when <paramref name="isInternalMember"/> is
    /// <see langword="true"/>).
    /// </summary>
    public static bool IsGeneratedAtLeastAsVisibleAs(
        INamedTypeSymbol generatedContainer,
        Accessibility generatedAccessibility,
        Accessibility memberAccessibility,
        INamedTypeSymbol targetContainer,
        bool isInternalMember = false) =>
        IsAtLeastAsVisibleAs(generatedContainer, memberAccessibility, targetContainer, isInternalMember) &&
        IsAsRestrictive(
            generatedAccessibility,
            generatedContainer,
            generatedContainer.ContainingAssembly,
            memberAccessibility,
            targetContainer,
            isInternalMember);

    /// <summary>
    /// Whether code anywhere in the defining assembly, and in assemblies it gives its internals to, can name
    /// <paramref name="type"/>: every constituent named type is <see cref="Accessibility.Public"/>,
    /// <see cref="Accessibility.Internal"/>, <see cref="Accessibility.ProtectedOrInternal"/>, or
    /// <see cref="Accessibility.NotApplicable"/>.
    /// </summary>
    public static bool IsAtLeastInternal(ITypeSymbol type) =>
        VisitType(type, 0, static (named, _) => named.DeclaredAccessibility is
            Accessibility.Public or
            Accessibility.Internal or
            Accessibility.ProtectedOrInternal or
            Accessibility.NotApplicable) is null;

    private static INamedTypeSymbol? FindTypeLessVisibleThan(
        ITypeSymbol type,
        Accessibility memberAccessibility,
        INamedTypeSymbol container,
        bool isInternalMember) =>
        VisitType(
            type,
            (MemberAccessibility: memberAccessibility, Container: container, IsInternalMember: isInternalMember),
            static (named, state) => IsAsRestrictive(
                named.DeclaredAccessibility,
                named.ContainingType,
                named.ContainingAssembly,
                state.MemberAccessibility,
                state.Container,
                state.IsInternalMember));

    // Mirrors Roslyn TypeSymbolExtensions.VisitType + IsTypeLessVisibleThan (canDigThroughNullable: true).
    // Returns the first constituent INamedTypeSymbol for which isAllowed returns false, or null.
    private static INamedTypeSymbol? VisitType<TState>(
        ITypeSymbol type,
        TState state,
        Func<INamedTypeSymbol, TState, bool> isAllowed)
    {
        while (true)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable &&
                nullable.TypeArguments.Length == 1)
            {
                type = nullable.TypeArguments[0];
                continue;
            }

            switch (type.TypeKind)
            {
                case TypeKind.Dynamic:
                case TypeKind.TypeParameter:
                    return null;

                case TypeKind.Array:
                    type = ((IArrayTypeSymbol)type).ElementType;
                    continue;

                case TypeKind.Pointer:
                    type = ((IPointerTypeSymbol)type).PointedAtType;
                    continue;

                case TypeKind.FunctionPointer:
                {
                    var signature = ((IFunctionPointerTypeSymbol)type).Signature;
                    if (signature.Parameters.Length == 0)
                    {
                        type = signature.ReturnType;
                        continue;
                    }

                    if (VisitType(signature.ReturnType, state, isAllowed) is { } failedReturn)
                    {
                        return failedReturn;
                    }

                    for (var i = 0; i < signature.Parameters.Length - 1; i++)
                    {
                        if (VisitType(signature.Parameters[i].Type, state, isAllowed) is { } failedParameter)
                        {
                            return failedParameter;
                        }
                    }

                    type = signature.Parameters[signature.Parameters.Length - 1].Type;
                    continue;
                }

                case TypeKind.Class:
                case TypeKind.Struct:
                case TypeKind.Interface:
                case TypeKind.Enum:
                case TypeKind.Delegate:
                case TypeKind.Submission:
                case TypeKind.Error:
                {
                    if (type is not INamedTypeSymbol named)
                    {
                        return null;
                    }

                    if (named.TypeKind is not (TypeKind.Error or TypeKind.Submission) &&
                        named.ContainingType is { } containing &&
                        VisitType(containing, state, isAllowed) is { } failedOuter)
                    {
                        return failedOuter;
                    }

                    if (named.TypeKind != TypeKind.Error && !isAllowed(named, state))
                    {
                        return named;
                    }

                    if (named.TypeKind is TypeKind.Enum or TypeKind.Submission)
                    {
                        return null;
                    }

                    if (named.IsAnonymousType)
                    {
                        foreach (var member in named.GetMembers())
                        {
                            if (member is IPropertySymbol property &&
                                VisitType(property.Type, state, isAllowed) is { } failedProperty)
                            {
                                return failedProperty;
                            }
                        }

                        return null;
                    }

                    var arguments = named.TypeArguments;
                    if (arguments.Length == 0)
                    {
                        return null;
                    }

                    for (var i = 0; i < arguments.Length - 1; i++)
                    {
                        if (VisitType(arguments[i], state, isAllowed) is { } failedArgument)
                        {
                            return failedArgument;
                        }
                    }

                    type = arguments[arguments.Length - 1];
                    continue;
                }

                default:
                    return null;
            }
        }
    }

    private static bool IsAsRestrictive(
        Accessibility acc1,
        INamedTypeSymbol? parent1,
        IAssemblySymbol? assembly1,
        Accessibility memberAccessibility,
        INamedTypeSymbol container,
        bool isInternalMember)
    {
        if (acc1 is Accessibility.Public or Accessibility.NotApplicable)
        {
            return true;
        }

        var containerAssembly = container.ContainingAssembly;

        if (isInternalMember && IsStepAsRestrictive(
                acc1, parent1, assembly1,
                Accessibility.Internal, null, containerAssembly))
        {
            return true;
        }

        if (IsStepAsRestrictive(acc1, parent1, assembly1, memberAccessibility, container, containerAssembly))
        {
            return true;
        }

        for (INamedTypeSymbol? s2 = container; s2 is not null; s2 = s2.ContainingType)
        {
            if (IsStepAsRestrictive(
                    acc1, parent1, assembly1,
                    s2.DeclaredAccessibility, s2.ContainingType, s2.ContainingAssembly))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStepAsRestrictive(
        Accessibility acc1,
        INamedTypeSymbol? parent1,
        IAssemblySymbol? assembly1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType,
        IAssemblySymbol? assembly2)
    {
        switch (acc1)
        {
            case Accessibility.Internal:
                return acc2 is Accessibility.Private or Accessibility.Internal or Accessibility.ProtectedAndInternal &&
                       HasInternalAccessTo(assembly2, assembly1);

            case Accessibility.ProtectedAndInternal:
                if (acc2 is not (Accessibility.Private
                        or Accessibility.Internal
                        or Accessibility.ProtectedAndInternal) ||
                    !HasInternalAccessTo(assembly2, assembly1))
                {
                    return false;
                }

                goto case Accessibility.Protected;

            case Accessibility.Protected:
                if (parent1 is null)
                {
                    return false;
                }

                if (acc2 == Accessibility.Private)
                {
                    for (var parent2 = s2ContainingType; parent2 is not null; parent2 = parent2.ContainingType)
                    {
                        if (IsAccessibleViaInheritance(parent1, parent2))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                return acc2 is Accessibility.Protected or Accessibility.ProtectedAndInternal &&
                       s2ContainingType is not null &&
                       IsAccessibleViaInheritance(parent1, s2ContainingType);

            case Accessibility.ProtectedOrInternal:
                if (parent1 is null)
                {
                    return false;
                }

                switch (acc2)
                {
                    case Accessibility.Private:
                        if (HasInternalAccessTo(assembly2, assembly1))
                        {
                            return true;
                        }

                        for (var parent2 = s2ContainingType; parent2 is not null; parent2 = parent2.ContainingType)
                        {
                            if (IsAccessibleViaInheritance(parent1, parent2))
                            {
                                return true;
                            }
                        }

                        return false;

                    case Accessibility.Internal:
                        return HasInternalAccessTo(assembly2, assembly1);

                    case Accessibility.Protected:
                        return s2ContainingType is not null &&
                               IsAccessibleViaInheritance(parent1, s2ContainingType);

                    case Accessibility.ProtectedAndInternal:
                        return HasInternalAccessTo(assembly2, assembly1) ||
                               (s2ContainingType is not null &&
                                IsAccessibleViaInheritance(parent1, s2ContainingType));

                    case Accessibility.ProtectedOrInternal:
                        return HasInternalAccessTo(assembly2, assembly1) &&
                               s2ContainingType is not null &&
                               IsAccessibleViaInheritance(parent1, s2ContainingType);

                    default:
                        return false;
                }

            case Accessibility.Private:
                if (acc2 != Accessibility.Private || parent1 is null)
                {
                    return false;
                }

                var parent1Original = parent1.OriginalDefinition;
                for (var parent2 = s2ContainingType; parent2 is not null; parent2 = parent2.ContainingType)
                {
                    if (SymbolEqualityComparer.Default.Equals(parent2.OriginalDefinition, parent1Original) ||
                        (parent1Original.TypeKind == TypeKind.Submission && parent2.TypeKind == TypeKind.Submission))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    // Roslyn's fromAssembly.HasInternalAccessTo(toAssembly) corresponds to toAssembly.GivesAccessTo(fromAssembly).
    private static bool HasInternalAccessTo(IAssemblySymbol? fromAssembly, IAssemblySymbol? toAssembly) =>
        fromAssembly is not null &&
        toAssembly is not null &&
        (SymbolEqualityComparer.Default.Equals(fromAssembly, toAssembly) ||
         toAssembly.GivesAccessTo(fromAssembly));

    internal static bool IsAccessibleViaInheritance(INamedTypeSymbol superType, INamedTypeSymbol subType)
    {
        var originalSuperType = superType.OriginalDefinition;
        for (INamedTypeSymbol? current = subType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, originalSuperType))
            {
                return true;
            }
        }

        if (originalSuperType.TypeKind == TypeKind.Interface)
        {
            foreach (var current in subType.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, originalSuperType))
                {
                    return true;
                }
            }
        }

        return superType.TypeKind == TypeKind.Submission && subType.TypeKind == TypeKind.Submission;
    }
}
