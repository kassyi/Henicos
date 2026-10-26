namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
public class StructConstructorTests : TranspilerTestBase
{
    private const int Count = 4;

    [Theory]
    [InlineData(nameof(StructConstructorKernels.ConstructorBodyKernel))]
    [InlineData(nameof(StructConstructorKernels.ConstructorDefaultArgumentKernel))]
    [InlineData(nameof(StructConstructorKernels.ConstructorNamedArgumentKernel))]
    [Scenario(TestScenario.SadPath)]
    public void Test_StructConstructorCall_IsNotEmittedAsAggregateInitialization(string kernel)
    {
        var (source, failures) = RoslynTestUtility.TranspileMethod(
            typeof(StructConstructorKernels), kernel, isDevice: false, dialect: CurrentDialect);

        // A constructor body, default value or argument name cannot survive S{a, b}: either the call must be lowered
        // to something that runs the constructor, or the compiler must report a diagnostic. The transpiler reports
        // through its failures, which the analyzer turns into diagnostics (S-46).
        Assert.True(
            !source.Contains("CtorScaled{") && !source.Contains("CtorScaled {") || failures.Count > 0,
            "new CtorScaled(...) became aggregate initialization without a diagnostic.");
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorBody_Runs()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorBodyKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((i * 10 + 5) * 100 + 2, output[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorDefaultArgument_IsApplied()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorDefaultArgumentKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i * 10 + 10, output[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorNamedArguments_AreMatchedByName()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorNamedArgumentKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            // CtorScaled(b: 1, a: tid): Sum = a * 10 + b
            // mutant: ObjectCreationHelper.AppendReorderedArguments passes the temporaries of a call whose parameters have one
            // type in the reverse parameter order -> this test fails
            Assert.Equal(i * 10 + 1, output[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorChainedThis_RunsTargetThenBody()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorChainedThisKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((i * 10 + 5) * 100 + 9, output[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorFieldAndPropertyInitializers_RunBeforeBody()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorFieldInitializersKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(107 + i, output[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ConstructorFieldAndPropertyInitializers_AreNotRunWithoutConstructorCall()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorFieldInitializersDefaultKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(10 + i, output[i]);
        }
    }
}
