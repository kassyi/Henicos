using System;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Differential and property tests for <c>Span&lt;T&gt;</c> and <c>Array</c> out-of-range diagnostics
/// and evaluation order (#731).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public partial class SpanWriteDiagnosticsTests : SpanKernelPathTestBase
{
    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void EvaluationOrder_ExpressionsWithSideEffects_EvaluatesLeftToRightWithSingleEvaluation(bool managed)
    {
        var src = CreateSequential(8, start: 10);
        var dst1 = new int[4];
        var dst2 = new int[6];
        var dst3 = new int[6];
        var counters = new int[4];

        var expectedDst1 = new int[4];
        var expectedDst2 = new int[6];
        var expectedDst3 = new int[6];
        var expectedCounters = new int[4];
        {
            var i = 1;
            src.AsSpan().Slice(i++, i++).CopyTo(expectedDst1.AsSpan());
            expectedCounters[0] = i;

            var j = 1;
            Array.Fill(expectedDst2, j * 10, j++, ++j);
            expectedCounters[1] = j;

            var k = 0;
            var len = 2;
            Array.Copy(src, k++, expectedDst3, k++, ++len);
            expectedCounters[2] = k;
            expectedCounters[3] = len;
        }

        Run(managed, () => SpanWriteKernels.LaunchEvaluationOrderSpanWriteKernel(
            src: src,
            targets: new EvaluationOrderSpanTargets
            {
                SliceCopyTarget = dst1,
                FillTarget = dst2,
                ArrayCopyTarget = dst3,
                Counters = counters,
            },
            gridDimX: GridDimX,
            blockDimX: BlockDimX));

        Assert.Equal(expectedCounters, counters);
        Assert.Equal(expectedDst1, dst1);
        Assert.Equal(expectedDst2, dst2);
        Assert.Equal(expectedDst3, dst3);
    }
}
