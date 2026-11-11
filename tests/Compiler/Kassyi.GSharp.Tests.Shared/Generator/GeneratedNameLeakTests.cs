using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Shared.Helpers;

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
[Collection(TestCollections.CorpusScan)]
public class GeneratedNameLeakTests
{
    private const string LeakId = "GCNR0001";

    private static readonly Lazy<HashSet<string>> s_systemVocabulary = new(BuildSystemVocabulary);

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void GeneratedCode_TranspilerEmission_DoesNotLeakRawUserIdentifiers()
    {
        var (entryModel, _) = RoslynTestUtility.GetLocalMethodContext(
            "GeneratedNameLeakTests",
            nameof(GeneratedCode_TranspilerEmission_DoesNotLeakRawUserIdentifiers));
        var compilation = entryModel.Compilation;
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
                    var failures = new List<TranspileFailure>();
                    var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
                    var source = MethodTranspiler.GenerateCudaSource(method, model, failures.Add, isDevice, dialect,
                        out _);
                    if (failures.Count > 0)
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

            if (!identifier.IsKind(SyntaxKind.None) && !s_systemVocabulary.Value.Contains(identifier.ValueText))
            {
                names.Add(identifier.ValueText);
            }
        }

        return names;
    }

    private static List<string> FindRawNames(string source, HashSet<string> declaredNames)
    {
        var results = new List<string>();
        var cleanSource = StripLiteralsAndComments(source);
        foreach (var lineSpan in cleanSource.AsSpan().EnumerateLines())
        {
            if (lineSpan.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            for (int i = 0; i < lineSpan.Length;)
            {
                if (char.IsAsciiDigit(lineSpan[i]))
                {
                    while (i < lineSpan.Length && (char.IsAsciiLetterOrDigit(lineSpan[i]) || lineSpan[i] == '_' || lineSpan[i] == '.'))
                    {
                        i++;
                    }
                }
                else if (char.IsAsciiLetter(lineSpan[i]) || lineSpan[i] == '_')
                {
                    int start = i;
                    while (i < lineSpan.Length && (char.IsAsciiLetterOrDigit(lineSpan[i]) || lineSpan[i] == '_'))
                    {
                        i++;
                    }

                    var id = lineSpan.Slice(start, i - start).ToString();
                    if (declaredNames.Contains(id) && !IsMemberPosition(lineSpan, start))
                    {
                        results.Add(id);
                    }
                }
                else
                {
                    i++;
                }
            }
        }

        return results;
    }

    private static string StripLiteralsAndComments(string text)
    {
        var chars = text.ToCharArray();
        int i = 0;
        int n = chars.Length;
        while (i < n)
        {
            if (chars[i] == '"' || chars[i] == '\'')
            {
                char quote = chars[i];
                chars[i] = ' ';
                i++;
                while (i < n && chars[i] != quote)
                {
                    if (chars[i] == '\\' && i + 1 < n)
                    {
                        chars[i] = ' ';
                        i++;
                    }

                    if (chars[i] != '\r' && chars[i] != '\n')
                    {
                        chars[i] = ' ';
                    }

                    i++;
                }

                if (i < n)
                {
                    chars[i] = ' ';
                    i++;
                }
            }
            else if (chars[i] == '/' && i + 1 < n && chars[i + 1] == '/')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i < n && chars[i] != '\n' && chars[i] != '\r')
                {
                    chars[i] = ' ';
                    i++;
                }
            }
            else if (chars[i] == '/' && i + 1 < n && chars[i + 1] == '*')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i += 2;
                while (i + 1 < n && !(chars[i] == '*' && chars[i + 1] == '/'))
                {
                    if (chars[i] != '\r' && chars[i] != '\n')
                    {
                        chars[i] = ' ';
                    }

                    i++;
                }

                if (i + 1 < n)
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                }
            }
            else
            {
                i++;
            }
        }

        return new string(chars);
    }

    private static bool IsMemberPosition(ReadOnlySpan<char> line, int index)
    {
        var i = index - 1;
        while (i >= 0 && line[i] == ' ')
        {
            i--;
        }

        return i >= 0 && (line[i] == '.' || (line[i] == '>' && i > 0 && line[i - 1] == '-') ||
                          (line[i] == ':' && i > 0 && line[i - 1] == ':'));
    }

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
            AddCalledNames(vocabulary, prelude);
            AddDeclaredTypeOrMacroNames(vocabulary, prelude);
        }

        foreach (var yaml in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.CompilerProject, "Dialects"), "*.yml"))
        {
            AddYamlIdentifiers(vocabulary, File.ReadAllText(yaml));
        }

        return vocabulary;
    }

    private static void AddCalledNames(HashSet<string> vocabulary, string text)
    {
        var span = text.AsSpan();
        for (int i = 0; i < span.Length;)
        {
            if (char.IsAsciiDigit(span[i]))
            {
                while (i < span.Length && (char.IsAsciiLetterOrDigit(span[i]) || span[i] == '_' || span[i] == '.'))
                {
                    i++;
                }
            }
            else if (char.IsAsciiLetter(span[i]) || span[i] == '_')
            {
                int start = i;
                while (i < span.Length && (char.IsAsciiLetterOrDigit(span[i]) || span[i] == '_'))
                {
                    i++;
                }

                int idEnd = i;
                while (i < span.Length && span[i] == ' ')
                {
                    i++;
                }

                if (i < span.Length && span[i] == '(')
                {
                    vocabulary.Add(span.Slice(start, idEnd - start).ToString());
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static void AddDeclaredTypeOrMacroNames(HashSet<string> vocabulary, string text)
    {
        string[] keywords = ["struct", "class", "union", "enum", "typedef", "using", "define"];
        var span = text.AsSpan();
        for (int i = 0; i < span.Length;)
        {
            if (char.IsAsciiDigit(span[i]))
            {
                while (i < span.Length && (char.IsAsciiLetterOrDigit(span[i]) || span[i] == '_' || span[i] == '.'))
                {
                    i++;
                }
            }
            else if (char.IsAsciiLetter(span[i]) || span[i] == '_')
            {
                int start = i;
                while (i < span.Length && (char.IsAsciiLetterOrDigit(span[i]) || span[i] == '_'))
                {
                    i++;
                }

                var word = span.Slice(start, i - start);
                bool isKeyword = false;
                foreach (var kw in keywords)
                {
                    if (word.SequenceEqual(kw.AsSpan()))
                    {
                        isKeyword = true;
                        break;
                    }
                }

                if (isKeyword)
                {
                    while (i < span.Length && (span[i] == ' ' || span[i] == '\t'))
                    {
                        i++;
                    }

                    if (i < span.Length && (char.IsAsciiLetter(span[i]) || span[i] == '_'))
                    {
                        int idStart = i;
                        while (i < span.Length && (char.IsAsciiLetterOrDigit(span[i]) || span[i] == '_'))
                        {
                            i++;
                        }

                        vocabulary.Add(span.Slice(idStart, i - idStart).ToString());
                    }
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static void AddYamlIdentifiers(HashSet<string> vocabulary, string yamlText)
    {
        var span = yamlText.AsSpan();
        for (int i = 0; i < span.Length;)
        {
            if (span[i] == ':')
            {
                i++;
                while (i < span.Length && (span[i] == ' ' || span[i] == '\t'))
                {
                    i++;
                }

                if (i < span.Length && span[i] == '"')
                {
                    i++;
                    int valStart = i;
                    while (i < span.Length && span[i] != '"')
                    {
                        i++;
                    }

                    var valSpan = span.Slice(valStart, i - valStart);
                    if (i < span.Length)
                    {
                        i++;
                    }

                    for (int j = 0; j < valSpan.Length;)
                    {
                        if (char.IsAsciiDigit(valSpan[j]))
                        {
                            while (j < valSpan.Length && (char.IsAsciiLetterOrDigit(valSpan[j]) || valSpan[j] == '_' || valSpan[j] == '.'))
                            {
                                j++;
                            }
                        }
                        else if (char.IsAsciiLetter(valSpan[j]) || valSpan[j] == '_')
                        {
                            int idStart = j;
                            while (j < valSpan.Length && (char.IsAsciiLetterOrDigit(valSpan[j]) || valSpan[j] == '_'))
                            {
                                j++;
                            }

                            vocabulary.Add(valSpan.Slice(idStart, j - idStart).ToString());
                        }
                        else
                        {
                            j++;
                        }
                    }
                }
            }
            else
            {
                i++;
            }
        }
    }
}
