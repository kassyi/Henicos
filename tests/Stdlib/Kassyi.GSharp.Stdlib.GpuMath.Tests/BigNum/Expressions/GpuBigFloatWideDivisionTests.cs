#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Float vector division of significands wider than the shared buffers of the division kernel (512 words): the
/// division works in global scratch and uses every word.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigFloatWideDivisionTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatWideDivisionTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // 600-word significands, past the 512 words of the division kernel's shared buffers: the quotient keeps the top
    // word instead of dividing the low 512 words.
    // mutant: evalWords clamped to DivModSharedWords again
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_Divide_SignificandsWiderThanTheSharedBuffers_UseEveryWord()
    {
        const int words = 600;
        var dividendWords = new uint[words];
        dividendWords[0] = 3;
        dividendWords[words - 1] = 1;
        var oneWords = new uint[words];
        oneWords[0] = 1;
        using var dividend = new GpuBigFloat(new GpuBigUInt(_workspace, dividendWords), 0, false);
        using var one = new GpuBigFloat(new GpuBigUInt(_workspace, oneWords), 0, false);
        using var array = new GpuBigFloatArray(_workspace, 2, words);
        array.SetElement(0, dividend);
        array.SetElement(1, dividend);

        using var byScalar = array.AsVectorizedEnumerable().SelectDivide(one);
        using var byItself = array.AsVectorizedEnumerable().ZipDivide(array.AsVectorizedEnumerable());
        var quotients = byScalar.Evaluate(_workspace);
        var ones = byItself.Evaluate(_workspace);

        var expected = (BigInteger.One << (32 * (words - 1))) + 3;
        AssertElement(quotients.GetElement(0), expected, "SelectDivide element 0");
        AssertElement(quotients.GetElement(1), expected, "SelectDivide element 1");
        AssertElement(ones.GetElement(0), 1, "ZipDivide element 0");
        AssertElement(ones.GetElement(1), 1, "ZipDivide element 1");
    }

    private static void AssertElement(scoped GpuBigFloatView element, BigInteger expected, string label)
    {
        var significand = element.Significand.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(significand, element.Exponent, element.IsNegative, expected, 1),
            $"{label}: expected {expected}, actual "
            + BigFloatReference.Describe(significand, element.Exponent, element.IsNegative));
    }
}
