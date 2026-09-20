using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_95_WarpShuffleAnalyzerTests : TranspilerTestBase
{
    private static readonly HostIntrinsicAnalyzer s_analyzer = new();

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-95")]
    public async Task Test_95_HostCallToShuffleDownSync_ReportsExpectedError()
    {
        // Asserting diagnostic error for host-side intrinsic use
        // Replace with actual analyzer if HostIntrinsicAnalyzer doesn't exist
        await RoslynTestUtility.TestAnalyzer("Analyzers/SYN-95_WarpShuffleSadPath.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0018");
    }
}
