using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution of <see cref="OutSpanParameterKernels"/>: a span parameter overwritten through an <c>out</c> argument after
/// a barrier or a shuffle reads the new span, as C# does (#1003).
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class OutSpanParameterTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SpanParameterOverwrittenThroughOut_ReadsTheNewSpan() => AssertOutSpan();

    // C#: both helpers read buffer[0] (99) through the span Fill wrote; the shuffle helper adds 0 + 1
    internal static void AssertOutSpan()
    {
        int[] input = [10, 20, 30, 40];
        int[] buffer = [99, 98];
        var result = new int[BlockSize];

        OutSpanParameterKernels.LaunchOutSpanKernel(input, buffer, result, blockDimX: BlockSize);

        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(99 * 1000 + 100, result[t]);
        }
    }
}
