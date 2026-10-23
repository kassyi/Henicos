using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Pins <c>GSharpNameEncoding.ComputeSourceHash</c>, whose value is persisted in the
/// <c>// gsharp-symbols: &lt;hash&gt;</c> header and in <c>*.gsym.json</c>.
/// </summary>
[Category(TestCategory.Unit)]
public class SourceHashTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("", "cbf29ce484222325")]
    [InlineData("int x;\n", "cebbb8a74bd80e97")]
    [InlineData("héllo\n日本語\r\n", "cb9d2b4a7af64f76")]
    public void ComputeSourceHash_GivenSource_ReturnsPinnedHash(string source, string expected)
    {
        Assert.Equal(expected, GSharpNameEncoding.ComputeSourceHash(source));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("int x;\r\n")]
    [InlineData("// gsharp-symbols: 0123456789abcdef\nint x;\n")]
    [InlineData("// gsharp-symbols: 0123456789abcdef\r\nint x;\r\n")]
    public void ComputeSourceHash_GivenCrlfOrHeader_EqualsLfBodyHash(string source)
    {
        Assert.Equal("cebbb8a74bd80e97", GSharpNameEncoding.ComputeSourceHash(source));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("// gsharp-symbols: abc", "cbf29ce484222325")]
    [InlineData("a\rb", "e5adb319040776a3")]
    [InlineData("a\r\r\nb", "4d4c4481d869115b")]
    public void ComputeSourceHash_GivenHeaderOnlyOrLoneCarriageReturn_ReturnsPinnedHash(string source, string expected)
    {
        // A header without a line break leaves an empty body; only a CR followed by LF is skipped.
        Assert.Equal(expected, GSharpNameEncoding.ComputeSourceHash(source));
    }
}
