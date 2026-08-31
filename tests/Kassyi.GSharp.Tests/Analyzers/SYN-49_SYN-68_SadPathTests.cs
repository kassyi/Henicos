using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_49_SYN_68_SadPathTests
{
    public static IEnumerable<string[]> GetSadPathTestCases()
    {
        yield return new[] { "SYN-49_UnsupportedInterlockedOverload.cs", "GSH0003" };
        yield return new[] { "SYN-68_UnsupportedMathMethod.cs", "GSH0003" };
        yield return new[] { "SYN-44_UnsupportedExtensionMethod.cs", "GSH0003" };
        yield return new[] { "SYN-46_UnsupportedWarpShuffleType.cs", "CS8377" };
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "SYN-44")]
    [Trait("FeatureId", "SYN-46")]
    [Trait("FeatureId", "SYN-49")]
    [Trait("FeatureId", "SYN-68")]
    [MemberData(nameof(GetSadPathTestCases))]
    public void Test_SadPath_ReportsDiagnosticAndPreventsCrash(string fileName, string expectedDiagnosticId)
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile(fileName);
        diagnostics.AssertContainsError(expectedDiagnosticId);
    }
}
