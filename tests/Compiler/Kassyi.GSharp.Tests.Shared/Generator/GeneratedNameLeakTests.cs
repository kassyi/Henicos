using Kassyi.GSharp.Compiler.Extensions;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// GCNR0001: transpiles every kernel and device function in the shared test sources on both dialects and fails when a
/// user-declared local, parameter, label or local function appears under its raw C# name, which means an emission
/// site bypassed <c>CppNameResolver</c>.
/// </summary>
/// <remarks>
/// GCNR is the test-only family for internal consistency checks; it never appears in <c>diagnostics.json</c>. Names that
/// the generated code itself legitimately uses (C++ keywords, prelude and dialect vocabulary) are skipped, as are
/// member accesses, string literals and comments.
/// </remarks>
public partial class GeneratedNameLeakTests
{
    private const string LeakId = "GCNR0001";

    [GeneratedRegex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex LiteralOrCommentRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex IdentifierRegex();

    private static readonly Lazy<HashSet<string>> s_systemVocabulary = new(BuildSystemVocabulary);

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_GeneratedCode_DoesNotLeakRawUserIdentifiers()
    {
        var compilation = MegaCompilationCache.Compilation;
        var leaks = new List<string>();
        var transpiled = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method) is not { } symbol || !IsGpuMethod(symbol))
                {
                    continue;
                }

                var declaredNames = CollectDeclaredNames(method);
                if (declaredNames.Count == 0)
                {
                    continue;
                }

                foreach (var dialect in new ITranspilerDialect[] { new GSharpCudaDialect(), new StandardCppDialect() })
                {
                    var diagnostics = new List<Diagnostic>();
                    var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
                    var source = MethodTranspiler.GenerateCudaSource(method, model, diagnostics.Add, isDevice, dialect,
                        out _);
                    if (diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        continue;
                    }

                    transpiled++;
                    foreach (var name in FindRawNames(source, declaredNames))
                    {
                        leaks.Add($"{symbol.ContainingType.Name}.{symbol.Name} ({dialect.GetType().Name}): {name}");
                    }
                }
            }
        }

        Assert.True(transpiled > 0, "No GPU method was transpiled; the scan did not run.");
        Assert.True(leaks.Count == 0,
            $"{LeakId}: raw user identifiers in generated code:" + Environment.NewLine +
            string.Join(Environment.NewLine, leaks.Distinct()));
    }

    private static bool IsGpuMethod(IMethodSymbol symbol) =>
        symbol.HasAttribute(KnownAttributes.GpuKernel) || symbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

    private static HashSet<string> CollectDeclaredNames(MethodDeclarationSyntax method)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in method.DescendantNodes())
        {
            var identifier = node switch
            {
                ParameterSyntax p => p.Identifier,
                VariableDeclaratorSyntax v => v.Identifier,
                SingleVariableDesignationSyntax d => d.Identifier,
                ForEachStatementSyntax f => f.Identifier,
                LocalFunctionStatementSyntax l => l.Identifier,
                LabeledStatementSyntax l => l.Identifier,
                _ => default
            };

            // Keyword-named declarations are covered by CppIdentifierRenameTests: a leak there fails to compile.
            if (!identifier.IsKind(SyntaxKind.None) && !CppNameFormatter.IsReservedCppKeyword(identifier.ValueText) &&
                !s_systemVocabulary.Value.Contains(identifier.ValueText))
            {
                names.Add(identifier.ValueText);
            }
        }

        return names;
    }

    private static IEnumerable<string> FindRawNames(string source, HashSet<string> declaredNames)
    {
        var code = LiteralOrCommentRegex().Replace(source, " ");
        foreach (var line in code.Split('\n'))
        {
            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            foreach (Match match in IdentifierRegex().Matches(line))
            {
                if (!declaredNames.Contains(match.Value) || IsMemberPosition(line, match.Index))
                {
                    continue;
                }

                yield return match.Value;
            }
        }
    }

    private static bool IsMemberPosition(string line, int index)
    {
        var i = index - 1;
        while (i >= 0 && line[i] == ' ')
        {
            i--;
        }

        return i >= 0 && (line[i] == '.' || (line[i] == '>' && i > 0 && line[i - 1] == '-') ||
                          (line[i] == ':' && i > 0 && line[i - 1] == ':'));
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])([A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex CalledNameRegex();

    [GeneratedRegex(@"\b(?:struct|class|union|enum|typedef|using|define)\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex DeclaredTypeOrMacroRegex();

    [GeneratedRegex(@":\s*""([^""]*)""")]
    private static partial Regex YamlMappingValueRegex();

    // Names that kernel output uses on its own: CUDA built-ins, prelude functions, types and macros, and the targets of
    // dialect type/intrinsic mappings. Parameter and local names inside the preludes are not included, because the
    // preludes are not part of the scanned output.
    private static HashSet<string> BuildSystemVocabulary()
    {
        var vocabulary = new HashSet<string>(StringComparer.Ordinal)
        {
            "threadIdx", "blockIdx", "blockDim", "gridDim", "warpSize"
        };

        foreach (var prelude in new[] { GSharpPreludeResources.Cpp, GSharpPreludeResources.Cuda })
        {
            AddGroups(vocabulary, CalledNameRegex(), prelude);
            AddGroups(vocabulary, DeclaredTypeOrMacroRegex(), prelude);
        }

        foreach (var yaml in Directory.EnumerateFiles(Path.Combine(GetCompilerRoot(), "Dialects"), "*.yml"))
        {
            foreach (Match mapping in YamlMappingValueRegex().Matches(File.ReadAllText(yaml)))
            {
                foreach (Match identifier in IdentifierRegex().Matches(mapping.Groups[1].Value))
                {
                    vocabulary.Add(identifier.Value);
                }
            }
        }

        return vocabulary;
    }

    private static void AddGroups(HashSet<string> vocabulary, Regex regex, string text)
    {
        foreach (Match match in regex.Matches(text))
        {
            vocabulary.Add(match.Groups[1].Value);
        }
    }

    private static string GetCompilerRoot([System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath) ?? AppContext.BaseDirectory);
        while (dir != null && dir.GetFiles("Henicos.slnx").Length == 0)
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root (Henicos.slnx).");
        }

        return Path.Combine(dir.FullName, "src", "Compiler", "Kassyi.GSharp.Compiler");
    }
}
