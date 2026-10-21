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

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(GSharpInternalNames.Context)]
    [InlineData(GSharpInternalNames.WarpExchange)]
    [InlineData(GSharpInternalNames.WarpShuffleCount)]
    [InlineData(GSharpInternalNames.AllContexts)]
    [InlineData(GSharpInternalNames.Root)]
    public void CppPrelude_ReferencesInternalName(string identifier)
    {
        Assert.True(ContainsIdentifier(GSharpPreludeResources.Cpp, identifier),
            $"The CPU prelude no longer references '{identifier}'; update WarpAndHints.hpp or GSharpInternalNames.");
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
