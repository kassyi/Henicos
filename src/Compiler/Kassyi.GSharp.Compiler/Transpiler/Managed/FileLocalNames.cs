namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Finds the names of file-local types in the bodies that the managed path copies into generated files (GSH0115): the
/// body of a kernel (its managed fallback), of a suspending device function (its state machine) and of a device
/// function with a managed version (#711).
/// </summary>
/// <remarks>
/// [WHY] A file-local type is visible only in the file that declares it, and C# has no way to name it from another
/// file, so a copy of a body that names one cannot compile. A value of a file-local type only comes from an expression
/// that names the type or a member of it: a member signature of a type that is not file-local cannot hold one (CS9051).
/// Finding the names is therefore enough.
/// </remarks>
internal static class FileLocalNames
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_copyBlocked = new();

    /// <summary>
    /// The file-local type that <paramref name="node"/> names in a copied body, or <see langword="null"/>: a type in or
    /// under a file-local type, or the file-local type of a static member or extension method that the name binds to.
    /// A name after a qualifier is left to the qualifier, and a name inside <c>nameof</c> to the string the managed path
    /// writes for it.
    /// </summary>
    public static INamedTypeSymbol? Find(SimpleNameSyntax node, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbol(node) is not { } symbol)
        {
            return null;
        }

        var isQualified = node.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name == node,
            QualifiedNameSyntax qualified => qualified.Right == node,
            MemberBindingExpressionSyntax or AliasQualifiedNameSyntax => true,
            _ => false
        };
        // x.Ext() is written as the static call of the extension's class, which the call does not spell
        var isReducedExtension = symbol is IMethodSymbol { ReducedFrom: not null };
        if (isQualified && !isReducedExtension || IsInNameof(node))
        {
            return null;
        }

        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { ReducedFrom: { } extension } => extension.ContainingType,
            IMethodSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol when symbol.IsStatic => symbol.ContainingType,
            _ => null
        };
        return GetFileLocalType(type);
    }

    /// <summary>
    /// Whether the managed path cannot copy the body of <paramref name="method"/>: it names a file-local type
    /// (<see cref="Find"/>), or its own type is in or under one, which the generated partial declaration would not
    /// reopen.
    /// </summary>
    public static bool BlocksCopy(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        return s_copyBlocked.For(compilation).GetOrAdd(definition, m => Evaluate(m, compilation));
    }

    /// <summary>
    /// The first function whose copy the managed fallback of <paramref name="kernel"/> needs and the managed path
    /// cannot write (<see cref="BlocksCopy"/>): the kernel itself, or a suspending device function it calls, directly or
    /// through others, whose state machine the fallback calls; <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// A device function with a managed version that cannot be written has none, and the managed bodies call the
    /// user's method instead (<see cref="Analysis.ManagedVersionAnalysis.HasManagedVersion"/>).
    /// </remarks>
    public static IMethodSymbol? FindBlockedCopy(IMethodSymbol kernel, Compilation compilation)
    {
        if (BlocksCopy(kernel, compilation))
        {
            return kernel;
        }

        if (!kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            return null;
        }

        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        return FindBlockedCallee(body, compilation, visited);
    }

    /// <summary><paramref name="method"/> for a GSH0115 message: <c>N.C.Run(int[])</c>.</summary>
    public static string FormatMethod(IMethodSymbol method) =>
        TypeLayoutCalculator.FormatTypeName(method.ContainingType) + "." + method.FormatSignature();

    private static IMethodSymbol? FindBlockedCallee(SyntaxNode body, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            var definition = callee.OriginalDefinition;
            if (!visited.Add(definition) ||
                !SuspensionAnalysis.TryGetDeviceFunctionBody(definition, compilation, out var declaration))
            {
                continue;
            }

            if (BlocksCopy(definition, compilation))
            {
                return definition;
            }

            if (declaration.GetBodyNode() is { } calleeBody &&
                FindBlockedCallee(calleeBody, compilation, visited) is { } blocked)
            {
                return blocked;
            }
        }

        return null;
    }

    private static bool Evaluate(IMethodSymbol method, Compilation compilation)
    {
        if (GetFileLocalType(method.ContainingType) is not null)
        {
            return true;
        }

        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        return declaration
            .DescendantNodes()
            .AsStackEnumerable()
            .OfType<SimpleNameSyntax>()
            .Any(model, static (name, semanticModel) => Find(name, semanticModel) is not null);
    }

    // The type itself when it is file-local, or the file-local type that contains it
    private static INamedTypeSymbol? GetFileLocalType(INamedTypeSymbol? type)
    {
        for (var current = type?.OriginalDefinition; current != null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsInNameof(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax identifier } &&
                identifier.Identifier.ValueText == "nameof")
            {
                return true;
            }

            if (current is StatementSyntax or MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
