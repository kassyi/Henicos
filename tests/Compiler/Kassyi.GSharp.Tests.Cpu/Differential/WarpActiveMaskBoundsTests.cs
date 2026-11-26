#nullable enable
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Unit tests for <see cref="WarpActiveMaskDifferentialTests.RunWithinLoopBounds"/>, verifying upper-bound mask
/// enforcement and exact scalar matching outside the bounded range.
/// </summary>
public sealed class WarpActiveMaskBoundsTests
{
    private static readonly DiffPathRunner[] s_cudaRunner =
    [
        new DiffPathRunner(DiffPath.Cuda, DiffPaths.DirectRunner)
    ];

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void RunWithinLoopBounds_OutputOutsideUpper_Throws()
    {
        uint[] classic = [0x00000001u];
        uint[] upper = [0x0000000Fu];

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            WarpActiveMaskDifferentialTests.RunWithinLoopBounds(
                1,
                static output => output[0] = 0x00000011u,
                classic,
                upper,
                static _ => 0,
                paths: s_cudaRunner));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void RunWithinLoopBounds_OutputWithinUpperWithOwnLane_Passes()
    {
        uint[] classic = [0x11111111u];
        uint[] upper = [0xFFFFFFFFu];

        WarpActiveMaskDifferentialTests.RunWithinLoopBounds(
            1,
            static output => output[0] = 0x00000011u,
            classic,
            upper,
            static _ => 0,
            paths: s_cudaRunner);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void RunWithinLoopBounds_ScalarOutsideBoundsRange_RequiresExactMatch()
    {
        uint[] classic = [0x11111111u, 4u];
        uint[] upper = [0xFFFFFFFFu, 4u];

        WarpActiveMaskDifferentialTests.RunWithinLoopBounds(
            2,
            static output =>
            {
                output[0] = 0x00000001u;
                output[1] = 4u;
            },
            classic,
            upper,
            static i => i == 0 ? 0 : 1,
            boundsFrom: 0,
            boundsTo: 1,
            paths: s_cudaRunner);
    }
}
