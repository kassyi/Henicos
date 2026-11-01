namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// The auto result array of a value-returning kernel (TestCases/Transpiler/Emission/SignatureShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    // The CUDA launcher copies the auto result back with the size it checked when it allocated the device buffer
    // (checked(__gsharp_auto_result.Length * sizeof(T))), not with a product computed again without the check (#889
    // review)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AutoResult_CudaCopyBack_UsesTheCheckedSize()
    {
        var generated = Generate("SignatureShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe global::System.Int32[] LaunchExpressionResultKernel(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The array launcher of ExpressionResultKernel is not generated.");
        var launcher = generated[start..generated.IndexOf("#pragma warning restore CS8500", start, StringComparison.Ordinal)];

        Assert.Contains(
            "int __gsharp_auto_result_size = checked(__gsharp_auto_result.Length * sizeof(global::System.Int32));",
            launcher,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if (__gsharp_auto_result_size > 0 && d___gsharp_auto_result != global::System.IntPtr.Zero)",
            launcher,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("int size = __gsharp_auto_result.Length", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "fixed (global::System.Int32* p___gsharp_auto_result = __gsharp_auto_result)",
            launcher,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("GCHandle.Alloc", launcher, StringComparison.Ordinal);
    }

    // CUDA array parameters, converted struct array parameters, and struct array fields compute byte sizes inside
    // checked(...), and the launcher finally block frees the auto result without CudaException.ThrowIfError (#1008)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaLauncher_ChecksArrayByteSizesAndFreesAutoResultWithoutThrowingInFinally()
    {
        var sigGenerated = Generate("SignatureShapeKernels", "Cuda");
        var structGenerated = Generate("StructShapeKernels", "Cuda");

        Assert.Contains(
            "= checked(input.Length * sizeof(global::System.Int32));",
            sigGenerated,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "= checked(pairs.Length * sizeof(",
            structGenerated,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "int size = checked(host.Table.Length * sizeof(global::System.Int32));",
            structGenerated,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "ThrowIfError(CudaDriverApi.cuMemFreeAsync",
            sigGenerated,
            StringComparison.Ordinal
        );
    }

    // An explicit-layout struct that converts member by member because of a later field copies only the widest arm
    // of each union offset group (#1006)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ExplicitLayout_ConvertedStructWithIdenticalUnion_CopiesOnlyWidestUnionArm(string dialect)
    {
        var generated = Generate("StructShapeKernels", dialect);
        Assert.Contains("native.L = host.L;", generated, StringComparison.Ordinal);
        Assert.Contains("host.L = native.L;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("native.I = host.I;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("host.I = native.I;", generated, StringComparison.Ordinal);
    }
}
