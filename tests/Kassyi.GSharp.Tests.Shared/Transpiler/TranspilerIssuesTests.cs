namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public struct GenericBox_IssuesTest<T> where T : unmanaged
{
    public T Item;
}

public class TranspilerIssuesTarget
{
    public static void GeneralTupleSupport(int[] data)
    {
        int x = data[0];
        int y = data[1];
        (x, y) = (y * 2, x + 3);
        data[0] = x;
        data[1] = y;
    }

    public static void LocalFunctionsSupport(int[] data, int factor)
    {
        int Compute(int val)
        {
            return val * factor + 1;
        }

        data[0] = Compute(data[0]);
    }

    public static void StackAllocFixedArray(int[] output)
    {
        Span<int> fixedBuf = stackalloc int[8];
        fixedBuf[0] = 42;
        output[0] = fixedBuf[0] + fixedBuf.Length;
    }

    public static void GenericsConstraintResolution(GenericBox_IssuesTest<float>[] boxes, float[] output)
    {
        GenericBox_IssuesTest<float> b = boxes[0];
        output[0] = b.Item;
    }
}

public class TranspilerIssuesTests : TranspilerTestBase
{
    [SkippableTheory]
    [Category(TestCategory.Compiler)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(10, 5, 2, 1)]
    [InlineData(10, 2, 5, 2)]
    [InlineData(2, 5, 1, 3)]
    [InlineData(2, 5, 3, 4)]
    public void Test_ComplexConditionalExpression(int x, int y, int z, int expectedResult)
    {
        int[] a = [x];
        int[] b = [y];
        int[] c = [z];
        int[] result = [0];

        Syntax.ControlFlowKernels.LaunchNestedTernaryKernel(a, b, c, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0]);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_GeneralTupleSupport()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TranspilerIssuesTarget>(
            nameof(TranspilerIssuesTarget.GeneralTupleSupport), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("auto __gsharp_tuple_0 = y*2;", cudaSource.WithCSharpNames());
        Assert.Contains("auto __gsharp_tuple_1 = x+3;", cudaSource.WithCSharpNames());
        Assert.Contains("x = __gsharp_tuple_0;", cudaSource.WithCSharpNames());
        Assert.Contains("y = __gsharp_tuple_1;", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_LocalFunctionsSupport()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TranspilerIssuesTarget>(
            nameof(TranspilerIssuesTarget.LocalFunctionsSupport), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("auto Compute = [&](int val)", cudaSource.WithCSharpNames());
        Assert.Contains("return val*factor+1;", cudaSource.WithCSharpNames());
        Assert.Contains("data[0]=Compute(data[0]);", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_StackAllocFixedArray()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TranspilerIssuesTarget>(
            nameof(TranspilerIssuesTarget.StackAllocFixedArray), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("int fixedBuf[8];", cudaSource.WithCSharpNames());
        Assert.Contains("fixedBuf[0]+8", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_GenericsConstraintResolution()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TranspilerIssuesTarget>(
            nameof(TranspilerIssuesTarget.GenericsConstraintResolution), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("template <typename T>", cudaSource.WithCSharpNames());
        Assert.Contains("struct Kassyi_GSharp_Tests_Shared_Transpiler_GenericBox_IssuesTest", cudaSource.WithCSharpNames());
        Assert.Contains("Kassyi_GSharp_Tests_Shared_Transpiler_GenericBox_IssuesTest<float> b = boxes[0];", cudaSource.WithCSharpNames());
    }
}
