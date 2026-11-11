using System.Globalization;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Keeps the hand-written C++ prelude in sync with the internal names that the C# emitters produce.
/// </summary>
public class PreludeInternalNamesTests
{
    private static readonly string[] s_legacyInternalNames =
        ["_ctx", "_state", "_yieldedForGridSync", "_warpExchange", "_all_contexts"];

    private static bool ContainsIdentifier(string source, string identifier)
    {
        var span = source.AsSpan();
        while (true)
        {
            int idx = span.IndexOf(identifier, StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            bool beforeOk = idx == 0 || (!char.IsAsciiLetterOrDigit(span[idx - 1]) && span[idx - 1] != '_');
            int afterIdx = idx + identifier.Length;
            bool afterOk = afterIdx >= span.Length || (!char.IsAsciiLetterOrDigit(span[afterIdx]) && span[afterIdx] != '_');

            if (beforeOk && afterOk)
            {
                return true;
            }

            span = span[(idx + 1)..];
        }
    }

    // The names that InternalNames.yml marks as declared by the Prelude.
    public static TheoryData<string, string> PreludeDeclaredNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var (path, value, preludeFile) in GSharpInternalNames.All)
        {
            if (preludeFile != null)
            {
                data.Add(path, value);
            }
        }

        return data;
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(PreludeDeclaredNames))]
    public void Prelude_DeclaresInternalName(string path, string identifier)
    {
        Assert.True(ContainsIdentifier(GSharpPreludeResources.Cpp, identifier) || ContainsIdentifier(GSharpPreludeResources.Cuda, identifier),
            $"No prelude declares '{identifier}' ({path}); update Resources/Prelude or InternalNames.yml.");
    }

    // The runtime cannot see GSharpInternalNames, so it spells the exported error accessor itself.
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CpuKernelLoader_LoadsThePreludeErrorAccessor()
    {
        var loader = File.ReadAllText(RepositoryPaths.Combine("src", "Runtime", "Kassyi.GSharp.Runtime", "Aot", "CpuKernelLoader.cs"));

        Assert.Contains($"\"{GSharpInternalNames.Prelude.ConsumeLastError}\"", loader);
        Assert.Contains($"\"{GSharpInternalNames.Prelude.ConsumeLastErrorDetails}\"", loader);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Preludes_ContainNoLegacyInternalNames()
    {
        foreach (var legacyName in s_legacyInternalNames)
        {
            Assert.False(ContainsIdentifier(GSharpPreludeResources.Cpp, legacyName),
                $"Legacy internal name '{legacyName}' found in C++ prelude.");
            Assert.False(ContainsIdentifier(GSharpPreludeResources.Cuda, legacyName),
                $"Legacy internal name '{legacyName}' found in CUDA prelude.");
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Preludes_DeclareIdenticalGSharpErrorCodeEnumValues()
    {
        var cppEntries = ParseErrorCodeEntries(GSharpPreludeResources.Cpp);
        var cudaEntries = ParseErrorCodeEntries(GSharpPreludeResources.Cuda);
        var managedNames = Enum.GetNames<GSharpErrorCode>();
        var managedValues = Enum.GetValues<GSharpErrorCode>();

        Assert.Equal(managedNames.Length, cppEntries.Count);
        Assert.Equal(managedNames.Length, cudaEntries.Count);

        for (var i = 0; i < managedNames.Length; i++)
        {
            var expectedName = managedNames[i];
            var expectedValue = (int)managedValues[i];
            Assert.Equal((expectedName, expectedValue), cppEntries[i]);
            Assert.Equal((expectedName, expectedValue), cudaEntries[i]);
        }
    }

    private static List<(string Name, int Value)> ParseErrorCodeEntries(string preludeSource)
    {
        int enumIdx = preludeSource.IndexOf("enum class GSharpErrorCode", StringComparison.Ordinal);
        Assert.True(enumIdx >= 0, "GSharpErrorCode enum declaration was not found in prelude.");
        int openBrace = preludeSource.IndexOf('{', enumIdx);
        Assert.True(openBrace >= 0, "Opening brace for GSharpErrorCode was not found.");
        int closeBrace = preludeSource.IndexOf('}', openBrace);
        Assert.True(closeBrace >= 0, "Closing brace for GSharpErrorCode was not found.");

        var body = preludeSource.AsSpan(openBrace + 1, closeBrace - openBrace - 1);
        var entries = new List<(string Name, int Value)>();
        foreach (var line in body.EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (trimmed.EndsWith(','))
            {
                trimmed = trimmed[..^1].Trim();
            }

            int eq = trimmed.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var name = trimmed[..eq].Trim().ToString();
            var value = int.Parse(trimmed[(eq + 1)..].Trim(), CultureInfo.InvariantCulture);
            entries.Add((name, value));
        }

        return entries;
    }
}
