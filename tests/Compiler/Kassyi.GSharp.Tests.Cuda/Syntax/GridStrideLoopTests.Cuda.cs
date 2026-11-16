using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class GridStrideLoopTests
{
    // 2^31 and 2^32 threads: too many for the CPU paths, which run every thread
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u << 21, 5000)]
    [InlineData(1u << 22, 5000)]
    public void Execute_HugeLaunch_VisitsEveryIndexOnce(uint gridX, int count) =>
        GridStrideLoopScenarios.CountVisitsOnAHugeLaunch(s_runNative, gridX, count);
}
