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
            "if (__gsharp_auto_result_size > 0 && d___gsharp_auto_result != IntPtr.Zero)",
            launcher,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("int size = __gsharp_auto_result.Length", launcher, StringComparison.Ordinal);
    }
}
