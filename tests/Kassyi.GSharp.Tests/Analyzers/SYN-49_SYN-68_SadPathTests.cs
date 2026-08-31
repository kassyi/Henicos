using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_49_SYN_68_SadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-49")]
    [Trait("Category", "SadPath")]
    public void Test_SYN49_UnsupportedInterlockedOverload_ReportsDiagnosticAndPreventsCrash()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-49_UnsupportedInterlockedOverload.cs");
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "SYN-68")]
    [Trait("Category", "SadPath")]
    public void Test_SYN68_UnsupportedMathMethod_ReportsDiagnosticAndPreventsCrash()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-68_UnsupportedMathMethod.cs");
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "SYN-44")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN44_UnsupportedExtensionMethod_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-44_UnsupportedExtensionMethod.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN46_UnsupportedWarpShuffleType_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-46_UnsupportedWarpShuffleType.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        diagnostics.AssertContainsError("GSH0002");
    }
}
