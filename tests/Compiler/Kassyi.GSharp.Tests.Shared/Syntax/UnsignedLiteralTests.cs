namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class UnsignedLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnsuffixedUIntLiteral_WrapsAsUInt32()
    {
        uint[] a = [2000000000u, 2147483648u, 3000000000u];
        uint[] quotients = new uint[a.Length];
        int[] comparisons = new int[a.Length];
        long[] widened = new long[a.Length];

        UnsignedLiteralKernels.LaunchUnsuffixedUIntLiteralKernel(a, quotients, comparisons, widened, blockDimX: (uint)a.Length);

        // 2000000000 + 3000000000 wraps to 705032704; a signed 64-bit sum would give 2500000000 after the division
        Assert.Equal([352516352u, 426258176u, 852516352u], quotients);
        // 2147483648 + 2147483648 wraps to 0, so the comparison is false for the second element only
        Assert.Equal([1, 0, 1], comparisons);
        // The uint sum wraps before it is widened to long
        Assert.Equal([4147483648L, 0L, 852516352L], widened);
    }
}
