namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Whether the managed state machine of a suspending device function can be emitted as valid C# into the partial type
/// that declares the function, and how visible it is (cross-assembly-device-code.md §3.7, #538 3rd review M1).
/// </summary>
/// <remarks>
/// [WHY] An assembly that exports its device code emits the state machines whatever its dialect, so a library that
/// built on main (CUDA, without kernels) must not stop building for them. A state machine is left out, and its record
/// says so (<c>DeviceExportRecord.ManagedStateMachine</c>, GSH0089 for a consumer on the C++ dialect), when it could not
/// compile: a containing type that is not partial, a type of its frame that is less visible than internal (a private
/// nested struct), or a suspending callee whose state machine is left out as well.
/// </remarks>
internal static class ManagedStateMachineAvailability
{
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IMethodSymbol, bool>> s_results = new();

    /// <summary>Whether the state machine of <paramref name="method"/> can be emitted (or, for a function of another assembly, was).</summary>
    public static bool CanEmit(IMethodSymbol method, Compilation compilation)
    {
        var results = s_results.GetValue(compilation,
            static _ => new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default));
        return CanEmit(method.OriginalDefinition, compilation, results,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>Whether <paramref name="method"/> and every type that contains it are public.</summary>
    public static bool IsEffectivelyPublic(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the state machine needs an unsafe context: the function is declared unsafe (or within an unsafe type),
    /// or its signature or body uses pointers.
    /// </summary>
    /// <remarks>Such a function already needs <c>AllowUnsafeBlocks</c> in its own project.</remarks>
    public static bool NeedsUnsafe(IMethodSymbol method, SyntaxNode declaration)
    {
        if (method.Parameters
                .AsStackEnumerable()
                .Any(static p => p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) ||
            method.ReturnType is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            return true;
        }

        for (var node = declaration; node != null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax member && member.Modifiers.Any(SyntaxKind.UnsafeKeyword))
            {
                return true;
            }
        }

        return declaration.DescendantNodes()
            .AsStackEnumerable()
            .Any(static node => node is PointerTypeSyntax or FunctionPointerTypeSyntax or UnsafeStatementSyntax
                or FixedStatementSyntax ||
                node is PrefixUnaryExpressionSyntax prefix &&
                prefix.Kind() is SyntaxKind.AddressOfExpression or SyntaxKind.PointerIndirectionExpression ||
                node.IsKind(SyntaxKind.PointerMemberAccessExpression));
    }

    private static bool CanEmit(IMethodSymbol method, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, bool> results, HashSet<IMethodSymbol> visiting)
    {
        if (results.TryGetValue(method, out var known))
        {
            return known;
        }

        // A cycle is not a device function G# emits (recursion is GSH0003); its result is the first function's
        if (!visiting.Add(method))
        {
            return true;
        }

        var result = Evaluate(method, compilation, results, visiting);
        visiting.Remove(method);
        return results.GetOrAdd(method, result);
    }

    private static bool Evaluate(IMethodSymbol method, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, bool> results, HashSet<IMethodSymbol> visiting)
    {
        // A function of another assembly: what its assembly emitted
        if (DeviceExportTable.IsOutside(method, compilation))
        {
            return DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
                   imported.Record.ManagedStateMachine;
        }

        if (!AreContainingTypesPartial(method) ||
            !method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } body ||
            !IsAtLeastInternal(method.ReturnType) ||
            !method.Parameters
                .AsStackEnumerable()
                .All(static p => IsAtLeastInternal(p.Type)))
        {
            return false;
        }

        // The locals become fields of the frame
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax &&
                model.GetDeclaredSymbol(node) is ILocalSymbol local && !IsAtLeastInternal(local.Type))
            {
                return false;
            }
        }

        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            if (!CanEmit(callee.OriginalDefinition, compilation, results, visiting))
            {
                return false;
            }
        }

        return true;
    }

    // The state machine is written into a partial declaration of each type that contains the function
    private static bool AreContainingTypesPartial(IMethodSymbol method)
    {
        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (!type.DeclaringSyntaxReferences
                    .AsStackEnumerable()
                    .Any(static reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                                             declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return false;
            }
        }

        return true;
    }

    // A type that an internal member of the assembly can name: public or internal, as are the types that contain it
    // and its type arguments
    private static bool IsAtLeastInternal(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAtLeastInternal(array.ElementType);
            case IPointerTypeSymbol pointer:
                return IsAtLeastInternal(pointer.PointedAtType);
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current != null; current = current.ContainingType)
                {
                    if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal
                        or Accessibility.ProtectedOrInternal or Accessibility.NotApplicable))
                    {
                        return false;
                    }
                }

                return named.TypeArguments
                    .AsStackEnumerable()
                    .All(IsAtLeastInternal);
            default:
                return true;
        }
    }
}
