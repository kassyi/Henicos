namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class StackAllocFixedArrayTarget
{
    public static void StackAllocHappyPath(int[] output)
    {
        Span<int> buf = stackalloc int[4];
        buf[0] = 10;
        buf[3] = 40;
        output[0] = buf[0] + buf[3] + buf.Length;
    }

    public static void StackAllocInTightLoop(int[] output)
    {
        int total = 0;
        for (int i = 0; i < 8; i++)
        {
            Span<int> scratch = stackalloc int[] { i, i * 2 };
            total += scratch[0] + scratch[1];
        }
        output[0] = total;
    }

    public static int ConsumeSpanHelper(ReadOnlySpan<int> values)
    {
        return values[0] + values.Length;
    }

    public static void StackAllocAsMethodArgument(int[] output)
    {
        ReadOnlySpan<int> localSpan = stackalloc int[] { 5, 10, 15 };
        output[0] = ConsumeSpanHelper(localSpan);
    }
}

public class StackAllocFixedArrayTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_GSH04_StackAlloc_HappyPath()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachStackAllocKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);

        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StackAllocFixedArrayTarget>(
            nameof(StackAllocFixedArrayTarget.StackAllocHappyPath), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] hostOutput = [0];
        StackAllocFixedArrayTarget.StackAllocHappyPath(hostOutput);
        Assert.Equal(54, hostOutput[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_GSH04_StackAlloc_InTightLoop()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StackAllocFixedArrayTarget>(
            nameof(StackAllocFixedArrayTarget.StackAllocInTightLoop), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] hostOutput = [0];
        StackAllocFixedArrayTarget.StackAllocInTightLoop(hostOutput);
        Assert.Equal(84, hostOutput[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_GSH04_StackAlloc_AsMethodArgument()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StackAllocFixedArrayTarget>(
            nameof(StackAllocFixedArrayTarget.StackAllocAsMethodArgument), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] hostOutput = [0];
        StackAllocFixedArrayTarget.StackAllocAsMethodArgument(hostOutput);
        Assert.Equal(8, hostOutput[0]);
    }
}
