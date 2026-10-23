namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
public class StructConstructorTests : TranspilerTestBase
{
    private const string KnownBugSkipReason = "Struct constructors become aggregate initialization (#474)";

    private const int Count = 4;

    [Theory(Skip = KnownBugSkipReason)]
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

    [SkippableFact(Skip = KnownBugSkipReason)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Test_ConstructorBody_Runs()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorBodyKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal((i + 5) * 100 + 2, output[i]);
        }
    }

    [SkippableFact(Skip = KnownBugSkipReason)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Test_ConstructorDefaultArgument_IsApplied()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorDefaultArgumentKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i + 10, output[i]);
        }
    }

    [SkippableFact(Skip = KnownBugSkipReason)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Test_ConstructorNamedArguments_AreMatchedByName()
    {
        var output = new int[Count];
        StructConstructorKernels.LaunchConstructorNamedArgumentKernel(output, blockDimX: Count);

        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i + 1, output[i]);
        }
    }
}
