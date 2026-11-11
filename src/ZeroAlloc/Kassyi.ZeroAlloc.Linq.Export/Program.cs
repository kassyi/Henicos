// Exports the StackLinq sources as an internal copy that builds for netstandard2.0, so that the G# compiler (a Roslyn
// analyzer) can use StackLinq without shipping another assembly. The net10.0 sources stay the only ones maintained.
// Usage: Kassyi.ZeroAlloc.Linq.Export <source dir> <output dir>
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
        if (!TryDropMembersThatNoLongerBuild(trees))
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
    private static bool TryDropMembersThatNoLongerBuild(List<SyntaxTree> trees)
    {
        var references = GetTrustedPlatformReferences();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        );

        for (var pass = 0; pass < 10; pass++)
        {
            var compilation = CSharpCompilation.Create("StackLinqExportCheck", trees, references, options);
            var dropped = CollectDroppedMembers(compilation);
            if (dropped is null)
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

    // The members that hold an error, per tree; null when an error lies outside any method or property
    private static Dictionary<SyntaxTree, HashSet<SyntaxNode>>? CollectDroppedMembers(CSharpCompilation compilation)
    {
        var dropped = new Dictionary<SyntaxTree, HashSet<SyntaxNode>>();
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
                return null;
            }

            RecordDroppedMember(dropped, tree, member, diagnostic);
        }

        return dropped;
    }

    private static MemberDeclarationSyntax? FindDroppableMember(SyntaxTree? tree, TextSpan sourceSpan) =>
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
            var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Console.WriteLine($"Dropped {file}({line}): {diagnostic.Id} {message}");
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
}
