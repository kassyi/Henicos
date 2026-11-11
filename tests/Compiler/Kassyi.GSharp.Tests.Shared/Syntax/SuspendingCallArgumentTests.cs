using System;
#if GSHARP_CPU
using Kassyi.GSharp.Runtime.Aot;
#endif
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A call that may suspend passes every parameter, whether the call names it, omits it or reorders it.</summary>
[Category(TestCategory.Syntax)]
public class SuspendingCallArgumentTests : TranspilerTestBase
{
    private const int Length = 64;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingCall_OmittedOptionalArguments_UsesDefaultValues() =>
        AssertOmittedOptionalArguments();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_NamedArguments_SkipsOptionalParameters() =>
        AssertNamedArgument();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_ReorderedNamedArguments_MatchesArgumentsByName() =>
        AssertReorderedNamedArguments();

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingCall_ManagedOmittedOptionalArguments_UsesDefaultValues() =>
        RunManaged(AssertOmittedOptionalArguments);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_ManagedNamedArguments_SkipsOptionalParameters() =>
        RunManaged(AssertNamedArgument);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_ManagedReorderedNamedArguments_MatchesArgumentsByName() =>
        RunManaged(AssertReorderedNamedArguments);

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

    private static void AssertOmittedOptionalArguments()
    {
        var input = CreateSequence();
        var output = new int[Length];

        SuspendingCallArgumentKernels.LaunchOmittedOptionalArgumentsKernel(input, output, blockDimX: Length);

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((input[i] + input[i ^ 1]) * 3 + 2, output[i]);
        }
    }

    private static void AssertNamedArgument()
    {
        var input = CreateSequence();
        var output = new int[Length];

        SuspendingCallArgumentKernels.LaunchNamedArgumentKernel(input, output, blockDimX: Length);

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(-((input[i] + input[i ^ 1]) * 3 + 2), output[i]);
        }
    }

    private static void AssertReorderedNamedArguments()
    {
        var input = CreateSequence();
        var output = new int[Length];

        SuspendingCallArgumentKernels.LaunchReorderedNamedArgumentsKernel(input, output, blockDimX: Length);

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((input[i] + 1 + input[i ^ 1] + 1) * 2 + 1, output[i]);
        }
    }

    private static int[] CreateSequence()
    {
        var values = new int[Length];
        for (int i = 0; i < Length; i++)
        {
            values[i] = i * 7 + 3;
        }

        return values;
    }
}
