using System.Globalization;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The generator runs in the build host's process and inherits its culture. Constant values must be written the same
/// way whatever that culture is: the output under a culture with a decimal comma (de-DE) or with U+2212 MINUS SIGN as
/// its negative sign (sv-SE) must equal the output under the invariant culture.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class ConstantLiteralCultureTests
{
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cuda", "de-DE")]
    [InlineData("Cuda", "sv-SE")]
    [InlineData("Cpu", "de-DE")]
    [InlineData("Cpu", "sv-SE")]
    public void Test_GeneratedSources_DoNotDependOnCurrentCulture(string dialect, string cultureName)
    {
        var source = RoslynTestUtility.ReadTestCase("Transpiler/ConstantLiteralCultureSource.cu.cs");

        var invariant = GenerateUnder(CultureInfo.InvariantCulture, source, dialect);
        var localized = GenerateUnder(new CultureInfo(cultureName), source, dialect);

        Assert.Equal(invariant.Keys.Order(StringComparer.Ordinal), localized.Keys.Order(StringComparer.Ordinal));
        foreach (var (hintName, text) in invariant)
        {
            Assert.True(
                text == localized[hintName],
                $"{hintName} differs under {cultureName}:{Environment.NewLine}{FirstDifference(text, localized[hintName])}");
        }
    }

    private static Dictionary<string, string> GenerateUnder(CultureInfo culture, string source, string dialect)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            var (driver, _, diagnostics) = RoslynTestUtility.RunGenerator(source, dialect);
            Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
            return driver.GetRunResult().Results
                .SelectMany(static r => r.GeneratedSources)
                .ToDictionary(static s => s.HintName, static s => s.SourceText.ToString(), StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // The line where the two texts part, so that the failure shows the literal instead of the whole file
    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Min(expectedLines.Length, actualLines.Length); i++)
        {
            if (expectedLines[i] != actualLines[i])
            {
                return $"line {i + 1}{Environment.NewLine}expected: {expectedLines[i].Trim()}{Environment.NewLine}actual:   {actualLines[i].Trim()}";
            }
        }

        return $"lengths differ: {expectedLines.Length} and {actualLines.Length} lines";
    }
}
