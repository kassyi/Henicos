namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
public class EnumTypeTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EnumStructFieldAndParameter_TranspilesToUnderlyingType()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod(
            typeof(EnumTypeKernels), nameof(EnumTypeKernels.EvaluatePacketsKernel), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("PacketColor", cudaSource);
        Assert.DoesNotContain("PacketFlags", cudaSource);
        Assert.Contains("int", cudaSource);
        Assert.Contains("unsigned int", cudaSource);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EvaluatePacketsKernel_ExecutesCorrectly()
    {
        const int count = 32;
        var items = new PacketItem[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = new PacketItem
            {
                Color = (PacketColor)(i % 3),
                Flags = (i % 2 == 0) ? PacketFlags.Active : PacketFlags.None,
                Payload = i * 10
            };
        }

        var output = new int[count];
        EnumTypeKernels.LaunchEvaluatePacketsKernel(
            items,
            PacketColor.Green,
            output,
            blockDimX: (uint)count);

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            int expected;
            if ((item.Flags & PacketFlags.Active) != PacketFlags.None)
            {
                int score = EnumTypeKernels.ScoreColor(item.Color);
                int bonus = item.Color == PacketColor.Green ? 100 : 0;
                expected = score + bonus + item.Payload;
            }
            else
            {
                expected = 0;
            }

            Assert.Equal(expected, output[i]);
        }
    }
}
