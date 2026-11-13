namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The file-local type that keeps the managed path from copying a body into a generated file, and the bodies that are
/// copied at all (GSH0119).
/// </summary>
/// <param name="Type">The file-local type that the copy would have to name.</param>
/// <param name="Body">The method whose copy names it: the method asked about, or a function its copy needs.</param>
/// <param name="Reason">Why <paramref name="Body"/> is copied, as the end of a sentence (<see cref="FileLocalNames.GetCopyReason"/>).</param>
internal readonly record struct FileLocalBlock(INamedTypeSymbol Type, IMethodSymbol Body, string Reason);

/// <summary>
/// Decides which bodies the managed path copies into generated files, and which of those name a file-local type, which
/// the copy cannot name (GSH0119).
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A file-local type is visible only in the file that declares it, and C# has no way to name it from another
/// file, so a copy of a body that names one cannot compile. A value of a file-local type only comes from an expression
/// that names the type or a member of it: a member signature of a type that is not file-local cannot hold one (CS9051).
/// Finding the names is therefore enough.
/// </para>
/// <para>
/// Most bodies are not copied at all: the managed path calls a device function as the user wrote it, unless it suspends
/// or holds a construct that only the managed path rewrites (<see cref="ManagedRewriteClassifier"/>), and runs a kernel
/// that needs no copy the way the C# execution does, one GPU thread after another. Such a body may name a file-local type.
/// </para>
/// </remarks>
internal static class FileLocalNames
{
    // "" for a body that needs no copy: the cache keeps no null
    private static readonly PerCompilationSymbolCache<IMethodSymbol, string> s_copyReasons = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, FileLocalBlock?> s_blocks = new();

    // [WHY] A file-local type can be named only in the file that declares it, so a body in a tree with none names none,
    // and a compilation with none has nothing to look for. Most projects have none; these keep their kernels from being
    // looked at (the answers are boxed: a ConditionalWeakTable keeps no value type)
    private static readonly ConditionalWeakTable<SyntaxTree, object> s_treeDeclares = new();

    private static readonly ConditionalWeakTable<Compilation, object> s_compilationDeclares = new();

    private static readonly object True = true;

    private static readonly object False = false;

    /// <summary>
    /// The file-local type that <paramref name="node"/> names in a body, or <see langword="null"/>: a type in or under a
    /// file-local type, or the file-local type of a static member or extension method that the name binds to. A name
    /// after a qualifier is left to the qualifier, and a name inside <c>nameof</c> to the string the managed path writes
    /// for it.
    /// </summary>
    public static INamedTypeSymbol? Find(SimpleNameSyntax node, SemanticModel semanticModel)
    {
        if (!DeclaresFileLocalType(node.SyntaxTree) || semanticModel.GetSymbol(node) is not { } symbol)
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
        if (isQualified && !isReducedExtension || IsInNameof(node, semanticModel))
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
    /// The file-local type whose extension <c>Deconstruct</c> a deconstruction (<c>var (a, b) = p;</c>) calls, or
    /// <see langword="null"/>. No name of the body binds to the extension, so <see cref="Find"/> cannot see it.
    /// </summary>
    public static INamedTypeSymbol? FindDeconstructor(AssignmentExpressionSyntax node, SemanticModel semanticModel) =>
        node.Left is TupleExpressionSyntax or DeclarationExpressionSyntax && DeclaresFileLocalType(node.SyntaxTree)
            ? FindDeconstructor(semanticModel.GetDeconstructionInfo(node))
            : null;

    private static INamedTypeSymbol? FindDeconstructor(DeconstructionInfo info)
    {
        // A nested deconstruction ((a, (b, c)) = p) has its own method per level; an assignment that deconstructs
        // nothing (a tuple of variables) has the default info
        if (!info.Nested.IsDefaultOrEmpty)
        {
            foreach (var nested in info.Nested)
            {
                if (FindDeconstructor(nested) is { } found)
                {
                    return found;
                }
            }
        }

        return info.Method is { } method ? GetFileLocalType((method.ReducedFrom ?? method).ContainingType) : null;
    }

    /// <summary>
    /// Why the managed path copies the body of <paramref name="method"/> into a generated file, as the end of a
    /// sentence, or <see langword="null"/> when it does not: the user's method is called as it is.
    /// </summary>
    /// <remarks>
    /// A device function is copied when it suspends (a state machine) or holds a construct that only the managed path
    /// rewrites, or calls a function that does (a managed version). A kernel is copied the same way, and also when it
    /// reaches anything that the C# execution cannot run one GPU thread at a time, which its managed fallback runs as
    /// a state machine.
    /// </remarks>
    public static string? GetCopyReason(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        var reasons = s_copyReasons.For(compilation);
        if (!reasons.TryGetValue(definition, out var reason))
        {
            reason = FindCopyReason(definition, compilation) ?? string.Empty;
            reasons.TryAdd(definition, reason);
        }

        return reason.Length == 0 ? null : reason;
    }

    private static string? FindCopyReason(IMethodSymbol method, Compilation compilation)
    {
        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !declaration.HasBody())
        {
            return null;
        }

        if (method.HasAttribute(KnownAttributes.GpuKernel))
        {
            if (CSharpExecutionAnalysis.FindObstacle(method, compilation) is { } obstacle)
            {
                return $"it reaches {obstacle}";
            }
        }
        else if (SuspensionAnalysis.RequiresStateMachine(method, compilation))
        {
            return "it suspends at a barrier or a warp exchange, which the managed path runs as a state machine";
        }

        if (ManagedVersionAnalysis.HasDirectRewrite(method, declaration, compilation))
        {
            return "its body holds a construct that only the managed path rewrites (an operator macro, a float " +
                   "StackLinq Sum or Gpu.InlinePtx)";
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var callee in ManagedVersionAnalysis.GetReferencedDeviceFunctions(declaration, model))
        {
            if (ManagedVersionAnalysis.Needs(callee, compilation))
            {
                return $"it calls '{FormatMethod(callee)}', which holds a construct that only the managed path rewrites";
            }
        }

        return null;
    }

    /// <summary>
    /// The first file-local type that the copy of <paramref name="method"/> would have to name, or
    /// <see langword="null"/> when the managed path copies no body of it or the copies name none: the method itself (its
    /// own type is in or under a file-local type, or its body names one), or a function whose copy it needs.
    /// </summary>
    public static FileLocalBlock? FindBlock(IMethodSymbol method, Compilation compilation)
    {
        if (!DeclaresFileLocalType(compilation))
        {
            return null;
        }

        var definition = method.OriginalDefinition;
        var blocks = s_blocks.For(compilation);
        if (!blocks.TryGetValue(definition, out var block))
        {
            block = FindBlock(definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
            blocks.TryAdd(definition, block);
        }

        return block;
    }

    /// <summary>
    /// Whether the managed path cannot copy the body of <paramref name="method"/> or a body its copy needs
    /// (<see cref="FindBlock"/>).
    /// </summary>
    public static bool BlocksCopy(IMethodSymbol method, Compilation compilation) =>
        FindBlock(method, compilation) is not null;

    /// <summary>
    /// The file-local type that the body of <paramref name="method"/> itself names, whether or not it is copied, or
    /// <see langword="null"/>.
    /// </summary>
    public static INamedTypeSymbol? FindOwnName(IMethodSymbol method, Compilation compilation)
    {
        if (!DeclaresFileLocalType(compilation))
        {
            return null;
        }

        if (GetFileLocalType(method.ContainingType) is { } container)
        {
            return container;
        }

        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return null;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var node in declaration.DescendantNodes())
        {
            var type = node switch
            {
                SimpleNameSyntax name => Find(name, model),
                AssignmentExpressionSyntax assignment => FindDeconstructor(assignment, model),
                _ => null
            };
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary><paramref name="method"/> for a GSH0119 message: <c>N.C.Run(int[])</c>.</summary>
    public static string FormatMethod(IMethodSymbol method) =>
        TypeLayoutCalculator.FormatTypeName(method.ContainingType) + "." + method.FormatSignature();

    private static FileLocalBlock? FindBlock(IMethodSymbol method, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(method) || GetCopyReason(method, compilation) is not { } reason)
        {
            return null;
        }

        if (FindOwnName(method, compilation) is { } type)
        {
            return new FileLocalBlock(type, method, reason);
        }

        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return null;
        }

        // A copy calls the copies of the functions it needs: their state machines and managed versions
        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var callee in ManagedVersionAnalysis.GetReferencedDeviceFunctions(declaration, model))
        {
            if (FindBlock(callee, compilation, visited) is { } blocked)
            {
                return blocked;
            }
        }

        return null;
    }

    // Whether a type declaration of the tree has the file modifier
    private static bool DeclaresFileLocalType(SyntaxTree tree) =>
        (bool)s_treeDeclares.GetValue(tree, static t => FindFileModifier(t) ? True : False);

    // Whether a tree of the compilation does
    private static bool DeclaresFileLocalType(Compilation compilation) =>
        (bool)s_compilationDeclares.GetValue(compilation, static c => AnyTreeDeclaresFileLocalType(c) ? True : False);

    private static bool AnyTreeDeclaresFileLocalType(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (DeclaresFileLocalType(tree))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FindFileModifier(SyntaxTree tree) =>
        tree.GetRoot()
            .DescendantNodes(static node =>
                node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
            .AsStackEnumerable()
            .OfType<MemberDeclarationSyntax>()
            .Any(static member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax &&
                                  member.Modifiers.Any(SyntaxKind.FileKeyword));

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

    // [WHY] The managed path writes nameof(...) as the string it is, so nothing in it names a type; a method of that
    // name is an ordinary call
    private static bool IsInNameof(SyntaxNode node, SemanticModel semanticModel)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax invocation &&
                ManagedNameQualifier.IsNameofOperator(invocation, semanticModel))
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
