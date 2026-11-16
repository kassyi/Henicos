#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// C# 14 first-class spans convert an array receiver to <see cref="ReadOnlySpan{T}"/> for an extension method call.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class FirstClassSpanTests : SpanKernelPathTestBase
{
    private const int ThreadCount = 32;

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void SpanReceiverKernel_ArrayReceiverExtensionCall_MatchesHostResult(bool managed)
    {
        var output = new int[ThreadCount];
        var expected = new int[ThreadCount];
        for (int t = 0; t < ThreadCount; t++)
        {
            int[] arr = [t, 7, 3];
            expected[t] = arr.FirstTwo();
        }

        Run(managed, () => FirstClassSpanKernels.LaunchSpanReceiverKernel(output, blockDimX: ThreadCount));

        Assert.Equal(expected, output);
    }
}
