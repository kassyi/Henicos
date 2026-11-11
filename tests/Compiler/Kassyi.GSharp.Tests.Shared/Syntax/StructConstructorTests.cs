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
    public void StructConstructorCall_UserDefinedConstructorInvocation_AvoidsAggregateInitializationAndRunsConstructor(
        string kernel)
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
    public void ConstructorBody_ExplicitConstructorInvocation_ExecutesConstructorBody()
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
    public void ConstructorDefaultArgument_OmittedOptionalParameter_AppliesDefaultArgumentValue()
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
    public void ConstructorNamedArguments_OutOfOrderNamedArguments_MatchesArgumentsByName()
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
    public void ConstructorChainedThis_ChainedThisConstructorInvocation_RunsTargetConstructorThenBody()
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
    public void
        ConstructorInitializers_FieldAndPropertyInitializersWithConstructor_RunsInitializersBeforeConstructorBody()
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
    public void
        ConstructorInitializers_DefaultOrUninitializedStructInstantiation_DoesNotRunInitializersWithoutConstructorCall()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorFieldInitializersDefaultKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(10 + i, output[i]);
        }
    }
}
