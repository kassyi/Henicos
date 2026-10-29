using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// Which array reads get the null check of #706 (TestCases/Transpiler/Emission/ArrayNullCheckShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    private const string ArrayRef = "__gsharp_array_ref(";

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

        Assert.Equal(4, Regex.Matches(kernel, Regex.Escape(ArrayRef)).Count);
        Assert.Contains(ArrayRef + "__gsharp_id_0_h.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayRef + "__gsharp_id_1_written, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayRef + "__gsharp_id_3_copy, ", kernel, StringComparison.Ordinal);
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
