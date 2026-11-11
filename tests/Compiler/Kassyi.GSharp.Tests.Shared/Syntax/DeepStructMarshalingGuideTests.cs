using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Runs the samples of sections 1 and 8 of the deep struct marshaling guide
/// (<c>docs/spec/en/04_guides/11_deep_struct_marshaling_guide.md</c>), so that the guide shows code that compiles and
/// runs.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class DeepStructMarshalingGuideTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FermatMegaKernel_NestedContextWithDefaultAndNamedArguments_WritesEveryArrayField() =>
        RunFermatMegaKernel();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ModernKernel_ContextArrayFieldsPinnedByFixed_PassesThemAsPointers() => RunModernKernel();

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void FermatMegaKernel_ManagedExecution_WritesEveryArrayField() => RunManaged(RunFermatMegaKernel);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ModernKernel_ManagedExecution_PassesThePinnedFieldsAsPointers() => RunManaged(RunModernKernel);

    private static void RunManaged(Action run)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            run();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif

    private static void RunFermatMegaKernel()
    {
        var ctx = new FermatContext
        {
            OperandA = [1, 2, 3, 4],
            OperandB = [10, 20, 30, 40],
            Result = new uint[4],
            Modulus = 17,
            Ntt = new DualNttContext
            {
                Buffer1 = [100, 200, 300, 400],
                Buffer2 = [5, 6, 7, 8],
                Twiddles = [1, 1, 2, 2],
                Length = 4,
            },
        };

        DeepStructMarshalingGuideKernels.LaunchFermatMegaKernel(ctx, blockDimX: 4);

        Assert.Equal([11u, 22u, 33u, 44u], ctx.Result);
        // Two calls, each adding Twiddles[i] + stage (0) + unrollFactor (4): the defaults reach the device function
        Assert.Equal([110u, 210u, 312u, 412u], ctx.Ntt.Buffer1);
        // Only the second call passes isInverse: true
        Assert.Equal([6u, 7u, 9u, 10u], ctx.Ntt.Buffer2);
    }

    private static void RunModernKernel()
    {
        var ctx = new CryptoContext
        {
            OperandA = [2, 3, 4, 5],
            OperandB = [7, 11, 13, 17],
            Result = new uint[4],
            Scratch = new uint[4],
            Length = 3,
        };

        DeepStructMarshalingGuideKernels.LaunchModernKernel(ctx, blockDimX: 4);

        // The fourth thread is past Length and writes nothing
        Assert.Equal([14u, 33u, 52u, 0u], ctx.Scratch);
        Assert.Equal([15u, 34u, 53u, 0u], ctx.Result);
    }
}
