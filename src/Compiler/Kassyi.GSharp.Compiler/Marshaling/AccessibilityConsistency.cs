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
                case TypeKind.Array:
                    type = ((IArrayTypeSymbol)type).ElementType;
                    continue;

                case TypeKind.Pointer:
                    type = ((IPointerTypeSymbol)type).PointedAtType;
                    continue;

                case TypeKind.FunctionPointer:
                {
                    if (VisitFunctionPointerHead(
                            ((IFunctionPointerTypeSymbol)type).Signature, state, isAllowed,
                            out var failedHead,
                            out var tail))
                    {
                        type = tail;
                        continue;
                    }

                    return failedHead;
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

                    if (VisitNamedTypeHead(named, state, isAllowed, out var failedNamed, out var tail))
                    {
                        type = tail;
                        continue;
                    }

                    return failedNamed;
                }

                default:
                    // Dynamic, TypeParameter and anything else has no constituent named type.
                    return null;
            }
        }
    }

    // Function pointer: return type first, then every parameter type; the last one is handed back to the caller's
    // loop (Roslyn's tail position). Returns true with `tail` set when the walk continues, false with `failed`
    // set (possibly null: nothing left to check) when it ends.
    private static bool VisitFunctionPointerHead<TState>(
        IMethodSymbol signature,
        TState state,
        Func<INamedTypeSymbol, TState, bool> isAllowed,
        out INamedTypeSymbol? failed,
        out ITypeSymbol tail)
    {
        failed = null;
        tail = signature.ReturnType;
        if (signature.Parameters.Length == 0)
        {
            return true;
        }

        if (VisitType(signature.ReturnType, state, isAllowed) is { } failedReturn)
        {
            failed = failedReturn;
            return false;
        }

        for (var i = 0; i < signature.Parameters.Length - 1; i++)
        {
            if (VisitType(signature.Parameters[i].Type, state, isAllowed) is { } failedParameter)
            {
                failed = failedParameter;
                return false;
            }
        }

        tail = signature.Parameters[signature.Parameters.Length - 1].Type;
        return true;
    }

    // Named type: containing type, the type itself, anonymous type properties, then type arguments (the last one is
    // the tail). Same contract as VisitFunctionPointerHead.
    private static bool VisitNamedTypeHead<TState>(
        INamedTypeSymbol named,
        TState state,
        Func<INamedTypeSymbol, TState, bool> isAllowed,
        out INamedTypeSymbol? failed,
        out ITypeSymbol tail)
    {
        failed = null;
        tail = named;
        if (named.TypeKind is not (TypeKind.Error or TypeKind.Submission) &&
            named.ContainingType is { } containing &&
            VisitType(containing, state, isAllowed) is { } failedOuter)
        {
            failed = failedOuter;
            return false;
        }

        if (named.TypeKind != TypeKind.Error && !isAllowed(named, state))
        {
            failed = named;
            return false;
        }

        if (named.TypeKind is TypeKind.Enum or TypeKind.Submission)
        {
            return false;
        }

        if (named.IsAnonymousType)
        {
            failed = FindFailedAnonymousProperty(named, state, isAllowed);
            return false;
        }

        var arguments = named.TypeArguments;
        if (arguments.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (VisitType(arguments[i], state, isAllowed) is { } failedArgument)
            {
                failed = failedArgument;
                return false;
            }
        }

        tail = arguments[arguments.Length - 1];
        return true;
    }

    private static INamedTypeSymbol? FindFailedAnonymousProperty<TState>(
        INamedTypeSymbol named,
        TState state,
        Func<INamedTypeSymbol, TState, bool> isAllowed)
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

    // Mirrors one iteration of Roslyn IsAsRestrictive (SymbolExtensions / TypeSymbolExtensions): is a type with
    // accessibility acc1 declared in parent1 as restrictive as a member with accessibility acc2 declared in
    // s2ContainingType.
    private static bool IsStepAsRestrictive(
        Accessibility acc1,
        INamedTypeSymbol? parent1,
        IAssemblySymbol? assembly1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType,
        IAssemblySymbol? assembly2) =>
        acc1 switch
        {
            Accessibility.Internal => IsInternalAsRestrictive(assembly1, acc2, assembly2),
            Accessibility.ProtectedAndInternal => IsProtectedAndInternalAsRestrictive(
                parent1, assembly1, acc2, s2ContainingType, assembly2),
            Accessibility.Protected => IsProtectedAsRestrictive(parent1, acc2, s2ContainingType),
            Accessibility.ProtectedOrInternal => IsProtectedOrInternalAsRestrictive(
                parent1, assembly1, acc2, s2ContainingType, assembly2),
            Accessibility.Private => IsPrivateAsRestrictive(parent1, acc2, s2ContainingType),
            _ => false,
        };

    private static bool IsInternalAsRestrictive(
        IAssemblySymbol? assembly1,
        Accessibility acc2,
        IAssemblySymbol? assembly2) =>
        acc2 is Accessibility.Private or Accessibility.Internal or Accessibility.ProtectedAndInternal &&
        HasInternalAccessTo(assembly2, assembly1);

    // Roslyn falls through from the private protected case into the protected case.
    private static bool IsProtectedAndInternalAsRestrictive(
        INamedTypeSymbol? parent1,
        IAssemblySymbol? assembly1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType,
        IAssemblySymbol? assembly2) =>
        acc2 is Accessibility.Private or Accessibility.Internal or Accessibility.ProtectedAndInternal &&
        HasInternalAccessTo(assembly2, assembly1) &&
        IsProtectedAsRestrictive(parent1, acc2, s2ContainingType);

    private static bool IsProtectedAsRestrictive(
        INamedTypeSymbol? parent1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType)
    {
        if (parent1 is null)
        {
            return false;
        }

        if (acc2 == Accessibility.Private)
        {
            return IsInheritedByAnyOuter(parent1, s2ContainingType);
        }

        return acc2 is Accessibility.Protected or Accessibility.ProtectedAndInternal &&
               s2ContainingType is not null &&
               IsAccessibleViaInheritance(parent1, s2ContainingType);
    }

    private static bool IsProtectedOrInternalAsRestrictive(
        INamedTypeSymbol? parent1,
        IAssemblySymbol? assembly1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType,
        IAssemblySymbol? assembly2)
    {
        if (parent1 is null)
        {
            return false;
        }

        var hasInternalAccess = HasInternalAccessTo(assembly2, assembly1);
        var viaInheritance = s2ContainingType is not null &&
                             IsAccessibleViaInheritance(parent1, s2ContainingType);
        return acc2 switch
        {
            Accessibility.Private => hasInternalAccess || IsInheritedByAnyOuter(parent1, s2ContainingType),
            Accessibility.Internal => hasInternalAccess,
            Accessibility.Protected => viaInheritance,
            Accessibility.ProtectedAndInternal => hasInternalAccess || viaInheritance,
            Accessibility.ProtectedOrInternal => hasInternalAccess && viaInheritance,
            _ => false,
        };
    }

    private static bool IsPrivateAsRestrictive(
        INamedTypeSymbol? parent1,
        Accessibility acc2,
        INamedTypeSymbol? s2ContainingType)
    {
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
    }

    // The loop over parent2 = s2ContainingType, its container, ... that the Roslyn protected cases share.
    private static bool IsInheritedByAnyOuter(INamedTypeSymbol parent1, INamedTypeSymbol? s2ContainingType)
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
