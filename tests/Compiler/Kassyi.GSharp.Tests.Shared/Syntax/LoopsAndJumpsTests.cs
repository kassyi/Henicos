namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class LoopsAndJumpsTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_ForeachInlineArray_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachInlineArrayKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_SwitchGotoCase_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<LoopsAndJumpsTarget>(
            nameof(LoopsAndJumpsTarget.SwitchGotoCase), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));
    }

    [Theory]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1, 35)] // case 1 -> goto case 2 -> goto default: 10 + 20 + 5
    [InlineData(2, 25)] // case 2 -> goto default: 20 + 5
    [InlineData(0, 5)]  // default only
    public void Test_SwitchGotoCase_Evaluation(int mode, int expected)
    {
        int[] output = [0];
        LoopsAndJumpsTarget.SwitchGotoCase(output, mode);
        Assert.Equal(expected, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_NamedBreakGoto_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<LoopsAndJumpsTarget>(
            nameof(LoopsAndJumpsTarget.NamedBreakGoto), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));
    }

    [Theory]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, 1)]     // exits on the very first iteration
    [InlineData(15, 15)]   // exits from the inner loop mid-way through the second outer iteration
    [InlineData(100, 100)] // exits exactly on the last iteration
    [InlineData(1000, 100)] // never jumps; both loops run to completion
    public void Test_NamedBreakGoto_Evaluation(int limit, int expected)
    {
        int[] output = [0];
        LoopsAndJumpsTarget.NamedBreakGoto(output, limit);
        Assert.Equal(expected, output[0]);
    }
}

public class LoopsAndJumpsTarget
{
    public static void SwitchGotoCase(int[] output, int mode)
    {
        int acc = 0;
        switch (mode)
        {
            case 1:
                acc += 10;
                goto case 2;
            case 2:
                acc += 20;
                goto default;
            default:
                acc += 5;
                break;
        }
        output[0] = acc;
    }

    public static void NamedBreakGoto(int[] output, int limit)
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                sum += 1;
                if (sum >= limit)
                {
                    goto ExitLoops;
                }
            }
        }

    ExitLoops:
        output[0] = sum;
    }
}

