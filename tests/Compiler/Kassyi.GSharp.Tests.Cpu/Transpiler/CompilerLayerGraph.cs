#nullable enable
using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler;

/// <summary>
/// The references that <see cref="CompilerLayerArchitectureTests"/> checks: each edge is
/// <c>source type (file) -&gt; target type (file)</c>, the target declared in <c>Transpiler/</c> or <c>Lowering/</c>.
/// </summary>
internal sealed record CompilerLayerGraph(
    ImmutableArray<string> CompilationErrors,
    ImmutableArray<string> DirectEdges,
    ImmutableArray<string> IndirectEdges)
{
    /// <summary>Compiles every compiler source and collects the direct and the indirect edges.</summary>
    public static CompilerLayerGraph Build()
    {
        var compilation = RoslynTestUtility.CreateCompilerFullSourceCompilation();
        var errors = new List<string>();
        foreach (var diagnostic in compilation.GetDeclarationDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                errors.Add(FormatDiagnostic(diagnostic));
            }
        }

        if (errors.Count > 0)
        {
            return new CompilerLayerGraph(errors.ToImmutableArray(), [], []);
        }

        var treeInfos = ClassifyTrees(compilation);
        var forbidden = CollectForbidden(compilation, treeInfos);
        var walk = new ReferenceWalk(compilation, treeInfos, forbidden.BySimpleName, errors);
        var tier1Types = CollectTier1Types(compilation, treeInfos, walk);

        var directEdges = CollectDirectEdges(tier1Types, walk.TypeReferences, forbidden.Types, treeInfos);
        var indirectEdges = CollectIndirectEdges(tier1Types, walk, forbidden.Types, treeInfos);
        return new CompilerLayerGraph(
            CompilationErrors: errors.ToImmutableArray(),
            DirectEdges: directEdges.Order(StringComparer.Ordinal).ToImmutableArray(),
            IndirectEdges: indirectEdges.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    private static Dictionary<SyntaxTree, TreeInfo> ClassifyTrees(CSharpCompilation compilation)
    {
        var compilerRoot = Path.GetFullPath(RepositoryPaths.CompilerProject);
        var treeInfos = new Dictionary<SyntaxTree, TreeInfo>(compilation.SyntaxTrees.Length);
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(tree.FilePath))
            {
                continue;
            }

            var relative = NormalizeRelativePath(compilerRoot, tree.FilePath);
            treeInfos[tree] = new TreeInfo(relative, ClassifyLayer(relative));
        }

        return treeInfos;
    }

    // The types declared in the forbidden folders, and those types and the extension methods there by simple name
    private static ForbiddenSymbols CollectForbidden(
        CSharpCompilation compilation,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var bySimpleName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var (tree, info) in treeInfos)
        {
            if (info.Layer == LayerKind.Forbidden)
            {
                CollectForbiddenDeclarations(compilation.GetSemanticModel(tree), tree, types, bySimpleName);
            }
        }

        var frozen = bySimpleName.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToImmutableArray(),
            StringComparer.Ordinal);
        return new ForbiddenSymbols(types, frozen);
    }

    private static void CollectForbiddenDeclarations(
        SemanticModel model,
        SyntaxTree tree,
        HashSet<INamedTypeSymbol> types,
        Dictionary<string, List<INamedTypeSymbol>> bySimpleName)
    {
        foreach (var node in tree.GetRoot().DescendantNodes(descendIntoTrivia: false))
        {
            if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax &&
                model.GetDeclaredSymbol(node) is INamedTypeSymbol typeSymbol)
            {
                var definition = typeSymbol.OriginalDefinition;
                types.Add(definition);
                if (IsAccessibleFromOtherFiles(definition))
                {
                    AddSimpleNameTarget(bySimpleName, definition.Name, definition);
                }
            }
            else if (node is MethodDeclarationSyntax methodSyntax &&
                     model.GetDeclaredSymbol(methodSyntax) is IMethodSymbol { IsExtensionMethod: true } method &&
                     IsAccessibleFromOtherFiles(method))
            {
                AddSimpleNameTarget(bySimpleName, method.Name, method.ContainingType.OriginalDefinition);
            }
        }
    }

    private static HashSet<INamedTypeSymbol> CollectTier1Types(
        CSharpCompilation compilation,
        Dictionary<SyntaxTree, TreeInfo> treeInfos,
        ReferenceWalk walk)
    {
        var tier1Types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (tree, info) in treeInfos)
        {
            if (info.Layer != LayerKind.Tier1)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes(descendIntoTrivia: false))
            {
                if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax &&
                    model.GetDeclaredSymbol(node) is INamedTypeSymbol declared)
                {
                    tier1Types.Add(declared.OriginalDefinition);
                }
            }

            walk.Walk(tree);
        }

        // A partial type of a gated folder may have parts elsewhere; their references count as its own
        foreach (var tier1Type in tier1Types)
        {
            walk.WalkDeclarations(tier1Type);
        }

        return tier1Types;
    }

    private static HashSet<string> CollectDirectEdges(
        HashSet<INamedTypeSymbol> tier1Types,
        Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> typeReferences,
        HashSet<INamedTypeSymbol> forbiddenTypes,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        var edges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in tier1Types)
        {
            if (!typeReferences.TryGetValue(source, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (forbiddenTypes.Contains(target))
                {
                    edges.Add(FormatEdge(source, target, treeInfos));
                }
            }
        }

        return edges;
    }

    // Breadth first from the gated types through every type outside the forbidden folders; an edge into a forbidden
    // folder from a type reached that way is indirect
    private static HashSet<string> CollectIndirectEdges(
        HashSet<INamedTypeSymbol> tier1Types,
        ReferenceWalk walk,
        HashSet<INamedTypeSymbol> forbiddenTypes,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        var edges = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<INamedTypeSymbol>(tier1Types, SymbolEqualityComparer.Default);
        var queue = new Queue<INamedTypeSymbol>(tier1Types);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            walk.WalkDeclarations(current);
            if (!walk.TypeReferences.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (forbiddenTypes.Contains(target))
                {
                    if (!tier1Types.Contains(current))
                    {
                        edges.Add(FormatEdge(current, target, treeInfos));
                    }
                }
                else if (!IsExcludedStateMachineType(target, treeInfos) && visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return edges;
    }

    private static string FormatDiagnostic(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        return $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)} ({lineSpan.Path}:{line})";
    }

    private static bool IsExcludedStateMachineType(
        INamedTypeSymbol type,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info) &&
                info.Layer == LayerKind.Tier1ExcludedStateMachine)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSimpleNameTarget(
        Dictionary<string, List<INamedTypeSymbol>> map,
        string name,
        INamedTypeSymbol target)
    {
        if (!map.TryGetValue(name, out var list))
        {
            list = [];
            map[name] = list;
        }

        list.Add(target);
    }

    private static bool IsAccessibleFromOtherFiles(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingType)
        {
            if (current is INamedTypeSymbol { IsFileLocal: true })
            {
                return false;
            }

            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or
                Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static LayerKind ClassifyLayer(string relative)
    {
        if (relative.StartsWith("Transpiler/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Lowering/", StringComparison.OrdinalIgnoreCase))
        {
            return LayerKind.Forbidden;
        }

        // The state machine of Ir/Linear is deleted with the syntax path; named one by one so that a new helper file
        // beside it is still checked
        if (relative is "Ir/Linear/IrStateMachine.cs" or "Ir/Linear/IrStateMachineBuilder.cs" or
            "Ir/Linear/IrStateMachineExit.cs")
        {
            return LayerKind.Tier1ExcludedStateMachine;
        }

        if (relative.StartsWith("Ir/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Semantics/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Dialects/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Backends/", StringComparison.OrdinalIgnoreCase))
        {
            return LayerKind.Tier1;
        }

        return LayerKind.Other;
    }

    private static string NormalizeRelativePath(string compilerRoot, string filePath)
    {
        var relative = Path.GetRelativePath(compilerRoot, Path.GetFullPath(filePath));
        return string.Create(relative.Length, relative, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] == '\\' ? '/' : source[i];
            }
        });
    }

    private static string FormatEdge(
        INamedTypeSymbol source,
        INamedTypeSymbol target,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        var sourceFile = GetPreferredRelativePath(source, treeInfos);
        var targetFile = GetPreferredRelativePath(target, treeInfos);
        return $"{FormatTypeName(source)} ({sourceFile}) -> {FormatTypeName(target)} ({targetFile})";
    }

    private static string GetPreferredRelativePath(
        INamedTypeSymbol type,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        string? best = null;
        var bestIsObj = true;

        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (!treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info))
            {
                continue;
            }

            var isObj = info.RelativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
            if (best is null ||
                (!isObj && bestIsObj) ||
                (isObj == bestIsObj && StringComparer.Ordinal.Compare(info.RelativePath, best) < 0))
            {
                best = info.RelativePath;
                bestIsObj = isObj;
            }
        }

        return best ?? string.Empty;
    }

    private static string FormatTypeName(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            chain.Add(current);
        }

        var builder = new StringBuilder();
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (i < chain.Count - 1)
            {
                builder.Append('.');
            }

            var part = chain[i];
            builder.Append(part.Name);
            if (part.TypeParameters.Length > 0)
            {
                builder.Append('<');
                for (var p = 0; p < part.TypeParameters.Length; p++)
                {
                    if (p > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(part.TypeParameters[p].Name);
                }

                builder.Append('>');
            }
        }

        return builder.ToString();
    }

    private enum LayerKind
    {
        Other,
        Tier1,
        Tier1ExcludedStateMachine,
        Forbidden
    }

    private readonly record struct TreeInfo(string RelativePath, LayerKind Layer);

    private readonly record struct ForbiddenSymbols(
        HashSet<INamedTypeSymbol> Types,
        Dictionary<string, ImmutableArray<INamedTypeSymbol>> BySimpleName);

    // Walks each syntax tree once and keeps the references of every type it declares
    private sealed class ReferenceWalk(
        CSharpCompilation compilation,
        Dictionary<SyntaxTree, TreeInfo> treeInfos,
        Dictionary<string, ImmutableArray<INamedTypeSymbol>> forbiddenBySimpleName,
        List<string> errors)
    {
        private readonly HashSet<SyntaxTree> _walkedTrees = [];

        public Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> TypeReferences { get; } =
            new(SymbolEqualityComparer.Default);

        public void Walk(SyntaxTree tree)
        {
            if (!_walkedTrees.Add(tree))
            {
                return;
            }

            // Binding every body again for its diagnostics doubles the run time; the walker binds each name anyway
            // and reports the ones that do not bind, which is what a missing source or generated file shows up as
            var model = compilation.GetSemanticModel(tree);
            var walker = new CompilerLayerReferenceWalker(model, forbiddenBySimpleName, TypeReferences, errors);
            walker.Visit(tree.GetRoot());
        }

        // The trees that declare a type, outside the forbidden folders and the excluded state machine
        public void WalkDeclarations(INamedTypeSymbol type)
        {
            foreach (var syntaxRef in type.DeclaringSyntaxReferences)
            {
                if (treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info) &&
                    info.Layer is not (LayerKind.Forbidden or LayerKind.Tier1ExcludedStateMachine))
                {
                    Walk(syntaxRef.SyntaxTree);
                }
            }
        }
    }
}
