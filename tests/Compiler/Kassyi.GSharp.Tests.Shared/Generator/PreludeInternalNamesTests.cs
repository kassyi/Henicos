using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Keeps the hand-written C++ prelude in sync with the internal names that the C# emitters produce.
/// </summary>
public partial class PreludeInternalNamesTests
{
    [GeneratedRegex(@"(?<![A-Za-z0-9_])_(ctx|state|yieldedForGridSync|warpExchange|all_contexts)(?![A-Za-z0-9_])")]
    private static partial Regex LegacyInternalNameRegex();

    [GeneratedRegex(@"enum\s+class\s+GSharpErrorCode\s*:\s*[A-Za-z0-9_]+\s*\{(?<body>[^}]+)\}", RegexOptions.Singleline)]
    private static partial Regex ErrorCodeEnumRegex();

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_]+)\s*=\s*(?<value>[0-9]+)")]
    private static partial Regex ErrorCodeEntryRegex();

    private static bool ContainsIdentifier(string source, string identifier) =>
        Regex.IsMatch(source, $"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])");

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
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cpp);
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cuda);
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
        var enumMatch = ErrorCodeEnumRegex().Match(preludeSource);
        Assert.True(enumMatch.Success, "GSharpErrorCode enum declaration was not found in prelude.");

        var entries = new List<(string Name, int Value)>();
        foreach (Match match in ErrorCodeEntryRegex().Matches(enumMatch.Groups["body"].Value))
        {
            entries.Add((match.Groups["name"].Value, int.Parse(match.Groups["value"].Value)));
        }

        return entries;
    }
}
