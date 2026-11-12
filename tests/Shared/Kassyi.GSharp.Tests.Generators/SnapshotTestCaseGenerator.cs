#nullable enable
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Generators;

[Generator]
public class SnapshotTestCaseGenerator : IIncrementalGenerator
{
    private const string RepositoryRootMarker = "Henicos.slnx";
    private const string OptInProperty = "GSharpGenerateSnapshotTestCases";

#pragma warning disable RS2008 // Test-only generator; not shipped, so analyzer release tracking does not apply.
    private static readonly DiagnosticDescriptor s_rootNotFoundDescriptor = new(
        id: "GSHT0001",
        title: "Repository root not found",
        messageFormat: "Could not find 'Henicos.slnx' in '{0}' or any parent directory; snapshot test cases were not generated",
        category: "GSharpTests",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    private const string SkipAttributeName = "SkipSnapshotConsistency";
    private const string SkipAttributeTypeName = SkipAttributeName + "Attribute";

    // The namespace a qualified [Kassyi.GSharp.Core.SkipSnapshotConsistency] names
    private static readonly NameSyntax s_coreNamespace = SyntaxFactory.ParseName("Kassyi.GSharp.Core");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The compiler and every test project load this assembly for its HNT analyzers; only the projects that compile
        // GeneratorSnapshotTests (Kassyi.GSharp.Tests.Cpu / .Cuda) opt in to the test case list
        var inputProvider = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projDir);
            options.GlobalOptions.TryGetValue($"build_property.{OptInProperty}", out var optIn);
            return (ProjectDir: projDir ?? string.Empty, Enabled: string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase));
        });

        context.RegisterSourceOutput(inputProvider, static (spc, input) =>
        {
            var (projDir, enabled) = input;
            if (!enabled)
            {
                return;
            }

            var rootDir = TryFindRepositoryRoot(projDir);
            if (rootDir is null)
            {
                var startingDir = string.IsNullOrEmpty(projDir) ? "<unknown>" : projDir;
                spc.ReportDiagnostic(Diagnostic.Create(s_rootNotFoundDescriptor, Location.None, startingDir));
                return;
            }

            var testCases = CollectSnapshotTestCases(rootDir);
            var source = EmitSnapshotTestCaseSource(testCases);
            spc.AddSource("SnapshotTestCaseSource.g.cs", source);
        });
    }

    private static string? TryFindRepositoryRoot(string projDir)
    {
        if (string.IsNullOrEmpty(projDir))
        {
            return null;
        }

        var dir = new DirectoryInfo(projDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepositoryRootMarker)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static List<string> CollectSnapshotTestCases(string rootDir)
    {
        string[] searchDirs =
        [
            Path.Combine(rootDir, "src"),
            Path.Combine(rootDir, "tests")
        ];

        var files = new List<KernelSourceFile>();
        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(searchDir, "*.cu.cs", SearchOption.AllDirectories))
            {
                if (!IsBuildOutput(file))
                {
                    files.Add(KernelSourceFile.Read(file));
                }
            }
        }

        // A type marked [SkipSnapshotConsistency] skips every file that declares a part of it
        var skippedTypes = new HashSet<string>(StringComparer.Ordinal);
        skippedTypes.UnionWith(files
            .SelectMany(static file => file.Types)
            .Where(static type => type.AttributeLists
                .SelectMany(static list => list.Attributes)
                .Any(IsSkipSnapshotConsistency)
            ).Select(GetFullName)
        );

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (IsEligibleKernelFile(file, skippedTypes))
            {
#pragma warning disable RS0030 // The separators of a Directory.GetFiles path are the subject: the case list keys use '/'
                var relativePath = file.Path.Substring(rootDir.Length).TrimStart('\\', '/').Replace('\\', '/');
#pragma warning restore RS0030
                unique.Add(relativePath);
            }
        }

        var sorted = new List<string>(unique);
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("/bin/") || file.Contains("/obj/");

    private static bool IsEligibleKernelFile(KernelSourceFile file, HashSet<string> skippedTypes)
    {
        var text = file.Text;
        if (!text.Contains("GpuKernel") && !text.Contains("CudaImport"))
        {
            return false;
        }

        if (text.Contains("[Fact]") || text.Contains("[Theory]") || text.Contains("[SkippableTheory]"))
        {
            return false;
        }

        return !file.Roots.Any(static root => root
                   .DescendantNodes()
                   .OfType<AttributeSyntax>()
                   .Any(IsSkipSnapshotConsistency)
               ) &&
               !file.Types.Any(type => skippedTypes.Contains(GetFullName(type)));
    }

    // The namespace-qualified name of a type, with its containing types and its arity (Ns.Outer.Inner`1)
    private static string GetFullName(TypeDeclarationSyntax type)
    {
        var parts = new List<string>();
        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case TypeDeclarationSyntax declaration:
                    var arity = declaration.TypeParameterList?.Parameters.Count ?? 0;
                    parts.Add(arity == 0
                        ? declaration.Identifier.ValueText
                        : declaration.Identifier.ValueText + "`" + arity);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Add(string.Join(".", ns.Name
                        .DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .Select(static identifier => identifier.Identifier.ValueText)
                    ));
                    break;
            }
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    // [SkipSnapshotConsistency] or [SkipSnapshotConsistencyAttribute], bare or qualified with Kassyi.GSharp.Core
    private static bool IsSkipSnapshotConsistency(AttributeSyntax attribute)
    {
        (string? Name, NameSyntax? Qualifier) parts = attribute.Name switch
        {
            IdentifierNameSyntax identifier => (identifier.Identifier.ValueText, null),
            QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified =>
                (right.Identifier.ValueText, qualified.Left),
            _ => (null, null)
        };

        return parts.Name is SkipAttributeName or SkipAttributeTypeName &&
               (parts.Qualifier is null || parts.Qualifier.IsEquivalentTo(s_coreNamespace));
    }

    private static string EmitSnapshotTestCaseSource(List<string> testCases)
    {
        var sb = new StringBuilder();
        foreach (var tc in testCases)
        {
            sb.AppendLine($"data.Add(\"{tc}\");");
        }

        return $$"""
            // <auto-generated/>
            using System;
            using System.Collections.Generic;
            using Xunit;

            namespace Kassyi.GSharp.Tests
            {
                public static class SnapshotTestCaseSource
                {
                    public static TheoryData<string> GetTestCases()
                    {
                        var data = new TheoryData<string>();
            """ + Environment.NewLine + sb + """
                    return data;
                }
            }
        }
        """;
    }

    // A kernel source file parsed once per dialect, so that the code under either branch of #if GSHARP_CPU is seen
    private sealed class KernelSourceFile(string path, string text, SyntaxNode[] roots)
    {
        private static readonly CSharpParseOptions[] s_dialectOptions =
        [
            new(LanguageVersion.Preview, preprocessorSymbols: ["GSHARP_CPU"]),
            new(LanguageVersion.Preview, preprocessorSymbols: ["GSHARP_CUDA"])
        ];

        public string Path { get; } = path;

        public string Text { get; } = text;

        public SyntaxNode[] Roots { get; } = roots;

        public IEnumerable<TypeDeclarationSyntax> Types =>
            Roots.SelectMany(static root => root.DescendantNodes().OfType<TypeDeclarationSyntax>());

        public static KernelSourceFile Read(string path)
        {
            var text = File.ReadAllText(path);
            var roots = new SyntaxNode[s_dialectOptions.Length];
            for (var i = 0; i < roots.Length; i++)
            {
#pragma warning disable RS0030 // The generator reads source files from disk that are not part of its compilation
                roots[i] = CSharpSyntaxTree.ParseText(text, s_dialectOptions[i]).GetRoot();
#pragma warning restore RS0030
            }

            return new KernelSourceFile(path, text, roots);
        }
    }
}
