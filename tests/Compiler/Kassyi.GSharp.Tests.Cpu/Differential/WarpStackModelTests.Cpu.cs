#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Cpu.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class WarpStackModelTests
{
    // Kernels of every kind of collective: shuffles in a device function, in a loop and in an unrolled body, votes and
    // reductions, matches, Gpu.SyncWarp, and Warp.ActiveMask() in a loop that some lanes continue and in one they leave
    // by a break, with the reconvergence regions of the state machine (a continue leaves the region of the body)
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
            WarpActiveMaskDifferentialKernels.ContinueInLoopKernel_Source,
            WarpActiveMaskDifferentialKernels.BreakInLoopKernel_Source,
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
        foreach (var source in new[]
                 {
                     WarpActiveMaskDifferentialKernels.ContinueInLoopKernel_Source,
                     WarpActiveMaskDifferentialKernels.BreakInLoopKernel_Source
                 })
        {
            Assert.Contains(GSharpInternalNames.StackWarp.ActiveMask, source, StringComparison.Ordinal);
            Assert.Contains(GSharpInternalNames.StackWarp.RegionEnter, source, StringComparison.Ordinal);
            Assert.Contains(GSharpInternalNames.StackWarp.Reconverge, source, StringComparison.Ordinal);
        }

        Assert.Contains(GSharpInternalNames.StackWarp.RegionLeave,
            WarpActiveMaskDifferentialKernels.ContinueInLoopKernel_Source, StringComparison.Ordinal);
    }

    // A continue that leaves the region of a loop body, and a jump to the join of a switch, are gotos in the stack
    // body. The temporary of a Warp.ActiveMask() they jump past is declared without its initializer, as C++ does not
    // let a jump pass an initialized local ([stmt.dcl]/3: a g++ error)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(nameof(WarpStackModelKernels.ContinuePastLocalKernel))]
    [InlineData(nameof(WarpActiveMaskDifferentialKernels.ContinueInLoopKernel))]
    [InlineData(nameof(WarpActiveMaskDifferentialKernels.ForwardGotoToSwitchJoinLabelKernel))]
    public void ActiveMaskGotos_StackModelBuild_JumpPastNoInitializedTemporary(string kernel)
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        var source = kernel switch
        {
            nameof(WarpStackModelKernels.ContinuePastLocalKernel) =>
                WarpStackModelKernels.ContinuePastLocalKernel_Source,
            nameof(WarpActiveMaskDifferentialKernels.ContinueInLoopKernel) =>
                WarpActiveMaskDifferentialKernels.ContinueInLoopKernel_Source,
            _ => WarpActiveMaskDifferentialKernels.ForwardGotoToSwitchJoinLabelKernel_Source
        };
        var crossed = new List<string>();
        var jumps = 0;
#pragma warning disable RS0030 // Category 5: the C++ text of the stack form, which has no syntax tree
        foreach (Match jump in Regex.Matches(source, @"goto (\w+);"))
        {
            var label = source.IndexOf(jump.Groups[1].Value + ":;", jump.Index, StringComparison.Ordinal);
            if (label < 0)
            {
                continue;
            }

            jumps++;
            var passed = source.AsSpan(jump.Index, label - jump.Index);
            if (passed.Contains("auto " + GSharpInternalNames.Lowering.WarpTempPrefix, StringComparison.Ordinal))
            {
                crossed.Add(jump.Value);
            }
        }
#pragma warning restore RS0030

        Assert.NotEqual(0, jumps);
        Assert.Empty(crossed);
    }
}
