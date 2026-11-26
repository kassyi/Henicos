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

    // A nullable local that a continue jumps past runs on stacks; a using declaration in a method that reaches
    // Warp.ActiveMask() keeps the kernel on the state machine (NativeStackObstacle.UsingDeclarationWithActiveMask)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ContinuePastKernels_StackModelBuild_RunOnStacksUnlessAUsingDeclarationEntersATry()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        Assert.Contains(GSharpInternalNames.NativeStack.Resume, WarpStackModelKernels.ContinuePastNullableKernel_Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(GSharpInternalNames.NativeStack.Resume,
            WarpStackModelKernels.ContinuePastUsingKernel_Source, StringComparison.Ordinal);
    }

    // A continue that leaves the region of a loop body, and a jump to the join of a switch, are gotos in the stack
    // body. The temporaries of a Warp.ActiveMask() or a ?: they jump past are declared without their initializer, and
    // a nullable local (a C++ default constructor) at the start of its scope, as C++ does not let a jump pass an
    // initialized local ([stmt.dcl]/3: a g++ error)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(nameof(WarpStackModelKernels.ContinuePastLocalKernel))]
    [InlineData(nameof(WarpActiveMaskDifferentialKernels.ContinueInLoopKernel))]
    [InlineData(nameof(WarpActiveMaskDifferentialKernels.ForwardGotoToSwitchJoinLabelKernel))]
    [InlineData(nameof(WarpStackModelKernels.ContinuePastNullableKernel))]
    public void ActiveMaskGotos_StackModelBuild_JumpPastNoInitializedTemporary(string kernel)
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        var source = kernel switch
        {
            nameof(WarpStackModelKernels.ContinuePastLocalKernel) =>
                WarpStackModelKernels.ContinuePastLocalKernel_Source,
            nameof(WarpActiveMaskDifferentialKernels.ContinueInLoopKernel) =>
                WarpActiveMaskDifferentialKernels.ContinueInLoopKernel_Source,
            nameof(WarpStackModelKernels.ContinuePastNullableKernel) =>
                WarpStackModelKernels.ContinuePastNullableKernel_Source,
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
            var passed = source[jump.Index..label];
            const string Temporaries =
                GSharpInternalNames.Lowering.WarpTempPrefix + "|" + GSharpInternalNames.Lowering.ConditionTempPrefix;
            foreach (Match declaration in Regex.Matches(passed,
                         $@"\n(auto (?:{Temporaries})\w+ =|System_Nullable<[^>]*> \w+[;=])"))
            {
                if (IsInScopeAtTheEnd(passed, declaration.Index))
                {
                    crossed.Add($"{jump.Value} passes {declaration.Groups[1].Value}");
                }
            }
        }
#pragma warning restore RS0030

        Assert.NotEqual(0, jumps);
        Assert.Empty(crossed);
    }

    // Whether a declaration at index of the C++ text between a goto and its label is still in scope at the label: no
    // brace that closes its block (a nested block, the body of a lambda) comes after it
    private static bool IsInScopeAtTheEnd(string passed, int index)
    {
        var depth = 0;
        for (var i = index; i < passed.Length; i++)
        {
            depth += passed[i] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };
            if (depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }
}
