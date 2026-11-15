#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for two-form CUDA kernel emission and dynamic selection (Issue #1940 / #1842).
/// Verifies aliased in/ref parameters, non-aliased parameters, multi-block execution, and Ptr launchers.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingTwoFormsDifferentialTests
{
    // T2: Non-aliased call (separate variables). Verifies by-value form execution matches C# oracle.
    internal static readonly DifferentialCase s_inAndRefSeparateVariables = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.InAndRefSeparateVariablesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 5;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var first = 5;
            var second = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernel(
                first, ref second, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = second;
        },
    };

    // T3: Multi-block (2 blocks, 64 threads). Thread (0, 0) writes to ref second, then reads in first.
    internal static readonly DifferentialCase s_multiBlockInAndRef = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.MultiBlockInAndRefKernel)))
    {
        Shape = LaunchShapes.Grid1D(gridX: 2, blockX: 64),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 15;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchMultiBlockInAndRefKernel(
                variable, ref variable, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

    // T4: ref readonly parameter aliased with ref parameter.
    internal static readonly DifferentialCase s_refReadOnlyAndRef = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.RefReadOnlyAndRefKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 15;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchRefReadOnlyAndRefKernel(
                variable, ref variable, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

    // T1: Existing aliased cases
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndRefOneVariable_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inAndRefOneVariable);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndOutOneVariable_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inAndOutOneVariable);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InFieldAndRefStruct_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inFieldAndRefStruct);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InStructAndRefField_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inStructAndRefField);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndRefConverted_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inAndRefConverted);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InElementAndItsArray_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inElementAndItsArray);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InElementAndItsSlice_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inElementAndItsSlice);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndTwoRefsOneVariable_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inAndTwoRefsOneVariable);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InReadBeforeOut_MatchesReference() =>
        DifferentialRunner.Run(MarshalingOverlapDifferentialTests.s_inReadBeforeOut);

    // T2: Non-aliased case
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndRefSeparateVariables_MatchesReference() =>
        DifferentialRunner.Run(s_inAndRefSeparateVariables);

    // T3: Multi-block case
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_MultiBlockInAndRef_MatchesReference() =>
        DifferentialRunner.Run(s_multiBlockInAndRef);

    // T4: ref readonly case
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_RefReadOnlyAndRef_MatchesReference() =>
        DifferentialRunner.Run(s_refReadOnlyAndRef);

    // T4: Launch...Ptr takes value type parameter T (public signature verified by passing int value directly)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TwoForms_LaunchPtr_AcceptsValueTypeParameter()
    {
#if GSHARP_CUDA
        var buffers = new DiffBuffers()
            .In("second", new int[] { 5 }, OutputPolicy.Exact)
            .Out<int>("observed", 1, OutputPolicy.Exact);
        using (var scope = buffers.PinOrUpload())
        {
            MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernelPtr(
                5, scope.PinOrUpload<int>("second"), scope.GetArray("observed"));
        }
        Assert.Equal(15, buffers.Get<int>("second")[0]);
        Assert.Equal(5, buffers.Get<int>("observed")[0]);
#else
        var second = 5;
        var observed = new int[1];
        fixed (int* pObserved = observed)
        {
            var pSecond = &second;
            var proxy = new GSharpArrayProxy { Data = (IntPtr)pObserved, Length = 1 };
            // Launch...Ptr compiles with value type int (first: 5), proving signature is T and not IntPtr.
            if (GpuExecution.Current == KernelExecution.Managed)
            {
                Assert.Throws<NotSupportedException>(() =>
                    MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernelPtr(
                        5, (IntPtr)pSecond, proxy));
                return;
            }

            MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernelPtr(
                5, (IntPtr)pSecond, proxy);

            Assert.Equal(1, proxy.Length);
            Assert.Equal(15, second);
            Assert.Equal(5, observed[0]);
        }
#endif
    }
}
