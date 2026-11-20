#nullable enable
using System;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Cpu.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class WarpStackModelTests
{
    // Kernels of every kind of collective but Warp.ActiveMask(): shuffles in a device function, in a loop and in an
    // unrolled body, votes and reductions, matches, and Gpu.SyncWarp
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WarpKernels_StackModelBuild_RunOnStacks()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        string[] sources =
        [
            WarpStackModelKernels.SkewedCallsKernel_Source,
            WarpStackModelKernels.ForUnrollSkewedShuffleKernel_Source,
            WarpStackModelKernels.StaleVisitsKernel_Source,
            WarpStackModelKernels.CallSiteFramesKernel_Source,
            WarpRendezvousDifferentialKernels.SubWarpShuffleInDeviceFunctionKernel_Source,
            WarpCollectiveDifferentialKernels.WarpVoteAndReduceKernel_Source,
            M1DiffWarpExtraKernels.WarpMatchAnyAndAllSyncKernel_Source,
            WarpRendezvousDifferentialKernels.SyncWarpExchangeKernel_Source,
        ];
        foreach (var source in sources)
        {
            Assert.Contains(GSharpInternalNames.NativeStack.Resume, source, StringComparison.Ordinal);
            Assert.Contains(GSharpInternalNames.StackWarp.Begin, source, StringComparison.Ordinal);
        }

        Assert.Contains(GSharpInternalNames.StackWarp.BallotSync,
            WarpCollectiveDifferentialKernels.WarpVoteAndReduceKernel_Source, StringComparison.Ordinal);
        Assert.Contains(GSharpInternalNames.StackWarp.ReduceAddSync,
            WarpCollectiveDifferentialKernels.WarpVoteAndReduceKernel_Source, StringComparison.Ordinal);
        Assert.Contains(GSharpInternalNames.StackWarp.MatchAllSync,
            M1DiffWarpExtraKernels.WarpMatchAnyAndAllSyncKernel_Source, StringComparison.Ordinal);
        Assert.Contains(GSharpInternalNames.StackWarp.SyncWarp,
            WarpRendezvousDifferentialKernels.SyncWarpExchangeKernel_Source, StringComparison.Ordinal);
    }
}
