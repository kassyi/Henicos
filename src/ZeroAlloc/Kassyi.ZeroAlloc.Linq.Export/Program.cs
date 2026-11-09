// Exports the StackLinq sources as an internal copy that builds for netstandard2.0, so that the G# compiler (a Roslyn
// analyzer) can use StackLinq without shipping another assembly. The net10.0 sources stay the only ones maintained.
// Usage: Kassyi.ZeroAlloc.Linq.Export <source dir> <output dir>
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static readonly string[] s_implicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.Diagnostics.CodeAnalysis"
    ];

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Kassyi.ZeroAlloc.Linq.Export <source dir> <output dir>");
            return 1;
        }

        var sourceDir = Path.GetFullPath(args[0]);
        var outputDir = Path.GetFullPath(args[1]);
        CleanOutputDirectory(outputDir);

        var trees = LoadAndRewriteSourceTrees(sourceDir);
        if (!DropMembersThatNoLongerBuild(trees))
        {
            return 1;
        }

        WriteExportedTrees(trees, outputDir);
        return 0;
    }

    private static void CleanOutputDirectory(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.cs"))
        {
            File.Delete(stale);
        }
    }

    private static List<SyntaxTree> LoadAndRewriteSourceTrees(string sourceDir)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>();
        foreach (var path in Directory.GetFiles(sourceDir, "*.cs"))
        {
            if (Path.GetFileName(path) == "GlobalUsings.cs")
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path);
            var root = (CompilationUnitSyntax)new NetStandardRewriter().Visit(tree.GetRoot());
            if (NetStandardRewriter.DeclaresType(root))
            {
                trees.Add(tree.WithRootAndOptions(AddImplicitUsings(root), parseOptions));
            }
        }

        return trees;
    }

    // The source project uses implicit usings; the copy states the ones it needs
    private static CompilationUnitSyntax AddImplicitUsings(CompilationUnitSyntax root)
    {
        var usings = root.Usings;
        foreach (var name in s_implicitUsings)
        {
            if (!HasUsingDirective(usings, name))
            {
                usings = usings.Add(
                    SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
                        .NormalizeWhitespace()
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                );
            }
        }

        return root.WithUsings(usings);
    }

    private static bool HasUsingDirective(SyntaxList<UsingDirectiveSyntax> usings, string name)
    {
        foreach (var existing in usings)
        {
            if (existing.Name is IdentifierNameSyntax or QualifiedNameSyntax &&
                SyntaxFactory.AreEquivalent(existing.Name, SyntaxFactory.ParseName(name)))
            {
                return true;
            }
        }

        return false;
    }

    // A member that calls a dropped member (Sum(selector) calls Sum()) no longer builds either. Compile the copy, drop
    // every member that holds an error and repeat until it builds.
    private static bool DropMembersThatNoLongerBuild(List<SyntaxTree> trees)
    {
        var references = GetTrustedPlatformReferences();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        );

        for (var pass = 0; pass < 10; pass++)
        {
            var compilation = CSharpCompilation.Create("StackLinqExportCheck", trees, references, options);
            if (!TryCollectDroppedMembers(compilation, out var dropped) || dropped is null)
            {
                return false;
            }

            if (dropped.Count == 0)
            {
                return true;
            }

            RemoveDroppedMembers(trees, dropped);
        }

        Console.Error.WriteLine("Cannot export: the copy still does not build after 10 passes.");
        return false;
    }

    private static List<MetadataReference> GetTrustedPlatformReferences()
    {
        var references = new List<MetadataReference>();
        var platform = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty).AsSpan();
        foreach (var range in platform.Split(Path.PathSeparator))
        {
            if (!platform[range].IsEmpty)
            {
                references.Add(MetadataReference.CreateFromFile(platform[range].ToString()));
            }
        }

        return references;
    }

    private static bool TryCollectDroppedMembers(
        CSharpCompilation compilation,
        out Dictionary<SyntaxTree, HashSet<SyntaxNode>>? dropped)
    {
        dropped = null;
        var map = new Dictionary<SyntaxTree, HashSet<SyntaxNode>>();
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            var tree = diagnostic.Location.SourceTree;
            var member = FindDroppableMember(tree, diagnostic.Location.SourceSpan);
            if (tree is null || member is null)
            {
                Console.Error.WriteLine($"Cannot export: {diagnostic}");
                return false;
            }

            RecordDroppedMember(map, tree, member, diagnostic);
        }

        dropped = map;
        return true;
    }

    private static MemberDeclarationSyntax? FindDroppableMember(
        SyntaxTree? tree,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan) =>
        tree?.GetRoot()
            .FindNode(sourceSpan)
            .FirstAncestorOrSelf<MemberDeclarationSyntax>(
                static node => node is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax
            );

    private static void RecordDroppedMember(
        Dictionary<SyntaxTree, HashSet<SyntaxNode>> dropped,
        SyntaxTree tree,
        MemberDeclarationSyntax member,
        Diagnostic diagnostic)
    {
        if (!dropped.TryGetValue(tree, out var members))
        {
            dropped[tree] = members = [];
        }

        if (members.Add(member))
        {
            var line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var file = Path.GetFileName(tree.FilePath);
            var msg = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Console.WriteLine($"Dropped {file}({line}): {diagnostic.Id} {msg}");
        }
    }

    private static void RemoveDroppedMembers(
        List<SyntaxTree> trees,
        Dictionary<SyntaxTree, HashSet<SyntaxNode>> dropped)
    {
        for (var i = 0; i < trees.Count; i++)
        {
            if (dropped.TryGetValue(trees[i], out var members))
            {
                var root = trees[i].GetRoot().RemoveNodes(members, SyntaxRemoveOptions.KeepNoTrivia)
                           ?? throw new InvalidOperationException("The export removed a whole file.");
                trees[i] = trees[i].WithRootAndOptions(root, trees[i].Options);
            }
        }
    }

    private static void WriteExportedTrees(List<SyntaxTree> trees, string outputDir)
    {
        var exported = 0;
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            var outputPath = Path.Combine(outputDir, Path.GetFileName(tree.FilePath));
            using var writer = new StreamWriter(outputPath);
            writer.Write("""
                // <auto-generated>
                // Exported from Kassyi.ZeroAlloc.Linq by Kassyi.ZeroAlloc.Linq.Export for netstandard2.0. Do not edit.
                // </auto-generated>
                #nullable enable

                """);
            root.WriteTo(writer);
            exported++;
        }

        Console.WriteLine($"Exported {exported} files to {outputDir}");
    }
    /// <summary>
    /// Rewrites one StackLinq file for netstandard2.0: no <c>allows ref struct</c> (it needs .NET 9 runtime support), so the
    /// ref structs become plain structs; members that only work with span enumerators, parallel enumeration or generic math
    /// are dropped; the types become internal in <see cref="ExportedNamespace"/>.
    /// </summary>
    private sealed class NetStandardRewriter() : CSharpSyntaxRewriter(visitIntoStructuredTrivia: false)
    {
        // A different namespace keeps the copy apart from the real assembly in projects that reference both
        private const string ExportedNamespace = "Kassyi.ZeroAlloc.Linq.Embedded";

        // Types that hold a span, so they must stay ref structs and cannot be type arguments without 'allows ref struct'
        private static readonly HashSet<string> s_droppedTypes = ["SpanEnumerator", "ParallelStackEnumerable"];

        // A member or an if branch that mentions one of these cannot build for netstandard2.0. The build check below runs
        // against the net10.0 platform, so an API that netstandard2.0 lacks (Vector.Sum) surfaces when the G# compiler builds
        // the copy; add its type here.
        private static readonly HashSet<string> s_droppedReferences =
        [
            "SpanEnumerator", "ParallelStackEnumerable", "CollectionsMarshal",
            "IAdditionOperators", "IAdditiveIdentity", "Vector"
        ];

        public static bool DeclaresType(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (node is BaseTypeDeclarationSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node) =>
            base.VisitFileScopedNamespaceDeclaration(
                node.WithName(SyntaxFactory.ParseName(ExportedNamespace).WithTriviaFrom(node.Name))
            );

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            if (s_droppedTypes.Contains(node.Identifier.ValueText))
            {
                return null;
            }

            // Every struct left holds enumerators and delegates only (span holders are dropped above), so all of them become
            // plain structs: StackEnumerable too, which SelectMany's selector returns through Func<T, TResult>
            return base.VisitStructDeclaration(node) is StructDeclarationSyntax visited
                ? visited.WithModifiers(Remove(MakeInternal(visited.Modifiers), SyntaxKind.RefKeyword))
                : null;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) =>
            base.VisitClassDeclaration(node) is ClassDeclarationSyntax visited
                ? visited.WithModifiers(MakeInternal(visited.Modifiers))
                : null;

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
            base.VisitInterfaceDeclaration(node) is InterfaceDeclarationSyntax visited
                ? visited.WithModifiers(MakeInternal(visited.Modifiers))
                : null;

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) =>
            KeepIfBuildable(base.VisitMethodDeclaration(node));

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) =>
            KeepIfBuildable(base.VisitConstructorDeclaration(node));

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node) =>
            KeepIfBuildable(base.VisitPropertyDeclaration(node));

        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) =>
            KeepIfBuildable(base.VisitFieldDeclaration(node));

        // where T : struct, IStackEnumerator<T>, allows ref struct  ->  where T : struct, IStackEnumerator<T>
        public override SyntaxNode? VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
        {
            var kept = new List<TypeParameterConstraintSyntax>(node.Constraints.Count);
            foreach (var constraint in node.Constraints)
            {
                if (constraint is not AllowsConstraintClauseSyntax)
                {
                    kept.Add(constraint);
                }
            }

            if (kept.Count == 0)
            {
                return null;
            }

            return kept.Count == node.Constraints.Count
                ? node
                : node.WithConstraints(SyntaxFactory.SeparatedList(kept)).NormalizeWhitespace().WithTriviaFrom(node);
        }

        // if (typeof(TEnumerator) == typeof(SpanEnumerator<T>)) { <fast path> } else <rest>  ->  <rest>
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!ReferencesDroppedSymbol(node.Condition))
            {
                return base.VisitIfStatement(node);
            }

            return node.Else is null ? null : Visit(node.Else.Statement).WithTriviaFrom(node);
        }

        // ArgumentNullException.ThrowIfNull(x);  ->  if (x is null) throw new ArgumentNullException(nameof(x));
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "ArgumentNullException" },
                        Name.Identifier.ValueText: "ThrowIfNull"
                    },
                    ArgumentList.Arguments: [{ Expression: IdentifierNameSyntax argument }]
                })
            {
                return base.VisitExpressionStatement(node);
            }

            var name = argument.Identifier.ValueText;
            return SyntaxFactory
                .ParseStatement($"if ({name} is null) throw new ArgumentNullException(nameof({name}));")
                .WithTriviaFrom(node);
        }

        // XML documentation may cite members that the export drops
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                ? default
                : base.VisitTrivia(trivia);

        // A member that still mentions a dropped symbol after the rewrite cannot build for netstandard2.0
        private static SyntaxNode? KeepIfBuildable(SyntaxNode? rewritten) =>
            rewritten is null || ReferencesDroppedSymbol(rewritten) ? null : rewritten;

        private static bool ReferencesDroppedSymbol(SyntaxNode node)
        {
            foreach (var descendant in node.DescendantNodes())
            {
                if (descendant is SimpleNameSyntax name && s_droppedReferences.Contains(name.Identifier.ValueText))
                {
                    return true;
                }
            }

            return false;
        }

        private static SyntaxTokenList MakeInternal(SyntaxTokenList modifiers)
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].IsKind(SyntaxKind.PublicKeyword))
                {
                    return modifiers.Replace(
                        modifiers[i],
                        SyntaxFactory.Token(SyntaxKind.InternalKeyword).WithTriviaFrom(modifiers[i])
                    );
                }
            }

            return modifiers;
        }

        private static SyntaxTokenList Remove(SyntaxTokenList modifiers, SyntaxKind kind)
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].IsKind(kind))
                {
                    return modifiers.RemoveAt(i);
                }
            }

            return modifiers;
        }
    }
}
