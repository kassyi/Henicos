using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Keeps the hand-written C++ prelude in sync with the internal names that the C# emitters produce.
/// </summary>
public partial class PreludeInternalNamesTests
{
    [GeneratedRegex(@"(?<![A-Za-z0-9_])_(ctx|state|yieldedForGridSync|warpExchange|all_contexts)(?![A-Za-z0-9_])")]
    private static partial Regex LegacyInternalNameRegex();

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

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Preludes_ContainNoLegacyInternalNames()
    {
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cpp);
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cuda);
    }
}
