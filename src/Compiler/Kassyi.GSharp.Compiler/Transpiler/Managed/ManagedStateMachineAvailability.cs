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
/// nested struct), a file-local type that its body names or that contains it (<see cref="FileLocalNames"/>), or a
/// suspending callee whose state machine is left out as well.
/// </remarks>
internal static class ManagedStateMachineAvailability
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_results = new();

    /// <summary>Whether the state machine of <paramref name="method"/> can be emitted (or, for a function of another assembly, was).</summary>
    public static bool CanEmit(IMethodSymbol method, Compilation compilation)
    {
        return CanEmit(method.OriginalDefinition, compilation, s_results.For(compilation),
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
                (node is PrefixUnaryExpressionSyntax prefix &&
                 prefix.Kind() is SyntaxKind.AddressOfExpression or SyntaxKind.PointerIndirectionExpression) ||
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
                   imported.Record.Traits.ManagedStateMachine;
        }

        if (!AreContainingTypesPartial(method) ||
            FileLocalNames.BlocksCopy(method, compilation) ||
            !method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } body ||
            !GeneratedMemberAccessibility.IsAtLeastInternal(method.ReturnType) ||
            !method.Parameters
                .AsStackEnumerable()
                .All(static p => GeneratedMemberAccessibility.IsAtLeastInternal(p.Type)))
        {
            return false;
        }

        // The locals become fields of the frame
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax &&
                model.GetDeclaredSymbol(node) is ILocalSymbol local &&
                !GeneratedMemberAccessibility.IsAtLeastInternal(local.Type))
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

    /// <summary>
    /// Whether every type that contains <paramref name="method"/> is declared <c>partial</c>: the state machines and the
    /// managed versions are written into a partial declaration of each one.
    /// </summary>
    internal static bool AreContainingTypesPartial(IMethodSymbol method)
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
}
