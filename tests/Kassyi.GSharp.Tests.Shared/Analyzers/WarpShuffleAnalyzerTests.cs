using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class WarpShuffleAnalyzerTests : TranspilerTestBase
{
    private static readonly HostIntrinsicAnalyzer s_analyzer = new();

    [SkippableFact]
    [Trait("Category", "SadPath")]
    public async Task Test_95_HostCallToShuffleDownSync_ReportsExpectedError()
    {
        // Asserting diagnostic error for host-side intrinsic use
        // Replace with actual analyzer if HostIntrinsicAnalyzer doesn't exist
        await RoslynTestUtility.TestAnalyzer("Analyzers/WarpShuffleSadPath.cs", s_analyzer)
                               .ReportsErrorsAsync("GSH0018");
    }

    [SkippableFact]
    [Trait("Category", "HappyPath")]
    public async Task Test_95_GpuCallToShuffleDownSync_ReportsNoError()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/WarpShuffleHappyPath.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }
}
