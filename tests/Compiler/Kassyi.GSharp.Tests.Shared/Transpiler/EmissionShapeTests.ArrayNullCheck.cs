using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// Which array reads get the null check of #706 (TestCases/Transpiler/Emission/ArrayNullCheckShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    private const string ArrayRef = "__gsharp_array_ref(";
    private const string ArrayAt = "__gsharp_array_at(";

    // A kernel array parameter that the kernel never writes (optional or not), a stack array local, a foreach over such a
    // parameter and a conditional between two of them are never null, so no read of them is checked
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_ProvenNonNullReceivers_AreNotChecked(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "ElidedKernel");

        Assert.DoesNotContain(ArrayRef, kernel, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrayAt, kernel, StringComparison.Ordinal);
    }

    // A field, a written kernel parameter, a local and a conditional with a null branch are checked once per read; a
    // conversion to a span and `is null` read nothing
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_FieldWrittenParameterAndLocal_AreCheckedAtEachRead(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "CheckedKernel");

        Assert.Equal(6, Regex.Matches(kernel, Regex.Escape(ArrayRef)).Count);
        Assert.Single(Regex.Matches(kernel, Regex.Escape(ArrayAt)));
        Assert.Contains(ArrayRef + "__gsharp_id_0_h.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayAt + "__gsharp_id_1_written, ", kernel, StringComparison.Ordinal);
        Assert.Matches(Regex.Escape(ArrayRef) + @"__gsharp_id_\d+_copy, ", kernel);
    }

    // A parameter written in a tuple deconstruction, through an out argument or inside a local function may hold null
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_ParametersWrittenInTupleOutOrLocalFunction_AreChecked(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "CheckedKernel");

        Assert.Contains(ArrayRef + "__gsharp_id_3_tupled, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayRef + "__gsharp_id_4_outWritten, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayRef + "__gsharp_id_5_fnWritten, ", kernel, StringComparison.Ordinal);
    }

    // The array launcher writes the flags word of every array argument, as the Ptr launcher does: 0 for a non-optional
    // array, whose stackalloc'd proxy would otherwise hold garbage under [SkipLocalsInit], and the null test for an
    // optional one
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayNullCheck_ArrayLauncher_WritesTheFlagsWordOfEveryArray()
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void LaunchElidedKernel(global::", StringComparison.Ordinal);
        Assert.True(start >= 0, "The array launcher of ElidedKernel is not generated.");
        var launcher = generated[start..generated.IndexOf("#pragma warning restore CS8500", start, StringComparison.Ordinal)];

        Assert.Contains("((int*)(__gsharp_proxy_0 + 1))[1] = 0;", launcher, StringComparison.Ordinal);
        Assert.Contains("((int*)(__gsharp_proxy_2 + 1))[1] = 0;", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "((int*)(__gsharp_proxy_1 + 1))[1] = bias is null ? global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.NullReadsAsEmpty : 0;",
            launcher,
            StringComparison.Ordinal
        );
    }

    // A device function parameter may be null: the foreach over it checks its source once, at the loop entry
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_ForEachOverDeviceFunctionParameter_ChecksTheSource(string dialect)
    {
        var source = Generate("ArrayNullCheckShapeKernels", dialect);

        Assert.Matches(@"for\(auto __gsharp_id_\d+_v : __gsharp_array_ref\(__gsharp_id_0_a, """"G#: a null array is read at [^""]*""""\)\)", source);
    }
}
