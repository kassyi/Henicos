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
/// Verifies aliased in/ref parameters, non-aliased parameters, multi-block execution,
/// asymmetric two-in parameter configurations, nested method/ctor parameter isolation, and Ptr launchers.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingTwoFormsDifferentialTests
{
    // Non-aliased execution: separate variables passed to in and ref parameters.
    // Verifies by-value form is dynamically selected and matches sequential C# oracle.
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

    // Multi-block execution (2 blocks x 64 threads = 128 threads): all threads read the in parameter
    // into allReads, while thread (0, 0) writes to ref second and reads first into observed[0].
    internal static readonly DifferentialCase s_multiBlockInAndRef = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.MultiBlockInAndRefKernel)))
    {
        Shape = LaunchShapes.Grid1D(gridX: 2, blockX: 64),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact)
            .Out<int>("allReads", 128, OutputPolicy.Ignore),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 15;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchMultiBlockInAndRefKernel(
                variable, ref variable, b.Get<int>("observed"), b.Get<int>("allReads"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

    // Ref readonly parameter aliased with ref parameter on the same variable.
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

    // Two in parameters where the first is aliased with ref third, and the second is separate.
    internal static readonly DifferentialCase s_twoInFirstAliased = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.TwoInAndOneRefKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 2, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 15;
            b.Get<int>("observed")[1] = 7;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var aliased = 5;
            var separate = 7;
            MarshalingTwoFormsDifferentialKernels.LaunchTwoInAndOneRefKernel(
                aliased, separate, ref aliased, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = aliased;
        },
    };

    // Two in parameters where the second is aliased with ref third, and the first is separate.
    internal static readonly DifferentialCase s_twoInSecondAliased = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.TwoInAndOneRefKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 2, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 7;
            b.Get<int>("observed")[1] = 15;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var separate = 7;
            var aliased = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchTwoInAndOneRefKernel(
                separate, aliased, ref aliased, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = aliased;
        },
    };

    // Nested method, local function, and struct constructor ordinal isolation (Item 1).
    // Verifies non-kernel method parameters with matching ordinals are not emitted as const T*.
    internal static readonly DifferentialCase s_nestedFunctionsAndCtor = new(
        KernelName(nameof(MarshalingTwoFormsDifferentialKernels.NestedFunctionsAndCtorKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 16;
            b.Get<int>("final")[0] = 16;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingTwoFormsDifferentialKernels.LaunchNestedFunctionsAndCtorKernel(
                variable, ref variable, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

#if GSHARP_CUDA
    // Only the CUDA launcher has two entry points: an unaliased launch does not resolve the by-address function, an
    // aliased one does. Kills both always-by-address and always-by-value mutants.
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_FormSelection_ObservedViaResolveCount()
    {
        var beforeUnaliased = MarshalingTwoFormsDifferentialKernels.__gsharp_hfunc_InAndRefSeparateVariablesKernel__gsharp_by_address.ResolveCount;
        var first = 5;
        var second = 5;
        var observed = new int[1];
        MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernel(first, ref second, observed);
        var afterUnaliased = MarshalingTwoFormsDifferentialKernels.__gsharp_hfunc_InAndRefSeparateVariablesKernel__gsharp_by_address.ResolveCount;

        // Unaliased launch must NOT resolve the by-address kernel function (by-value form was launched)
        Assert.Equal(beforeUnaliased, afterUnaliased);

        // Aliased launch must resolve the by-address kernel function
        var variable = 5;
        MarshalingTwoFormsDifferentialKernels.LaunchInAndRefSeparateVariablesKernel(variable, ref variable, observed);
        var afterAliased = MarshalingTwoFormsDifferentialKernels.__gsharp_hfunc_InAndRefSeparateVariablesKernel__gsharp_by_address.ResolveCount;
        Assert.Equal(beforeUnaliased + 1, afterAliased);
    }
#endif

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_InAndRefSeparateVariables_MatchesReference() =>
        DifferentialRunner.Run(s_inAndRefSeparateVariables);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_MultiBlockInAndRef_MatchesReference() =>
        DifferentialRunner.Run(s_multiBlockInAndRef);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_RefReadOnlyAndRef_MatchesReference() =>
        DifferentialRunner.Run(s_refReadOnlyAndRef);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_TwoInFirstAliased_MatchesReference() =>
        DifferentialRunner.Run(s_twoInFirstAliased);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_TwoInSecondAliased_MatchesReference() =>
        DifferentialRunner.Run(s_twoInSecondAliased);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoForms_NestedFunctionsAndCtor_MatchesReference() =>
        DifferentialRunner.Run(s_nestedFunctionsAndCtor);

    // Launch...Ptr takes value type parameter T (public signature verified by passing int value directly)
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
