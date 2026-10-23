namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies the parts of a kernel's launch wrappers that come from single parameters: the array copy direction from
/// <c>[In]</c> / <c>[Out]</c>, and the default value literals.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelParameterAbiTests
{
    private static readonly string s_copyDirectionKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelParameterAbi_CopyDirection.cu.cs");

    private static readonly string s_defaultValueKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelParameterAbi_DefaultValues.cu.cs");

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("inOnly", true, false)]
    [InlineData("outOnly", false, true)]
    [InlineData("both", true, true)]
    [InlineData("neither", true, true)]
    public void Test_ArrayCopyDirection_FollowsInAndOut(string parameter, bool copyIn, bool copyOut)
    {
        var wrapper = GetWrapper(s_copyDirectionKernels, "Cuda", "CopyKernels");

        Assert.Equal(!copyIn, wrapper.Contains($"// Skip HtoD copy for [Out] array: {parameter}\n", StringComparison.Ordinal));
        Assert.Equal(!copyOut, wrapper.Contains($"// Skip DtoH copy for [In] array: {parameter}\n", StringComparison.Ordinal));
        Assert.Equal(copyOut, wrapper.Contains($"// Copy back array: {parameter}\n", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ArrayCopyDirection_IgnoresAUserInAttribute()
    {
        var wrapper = GetWrapper(s_copyDirectionKernels, "Cuda", "CopyKernels");

        // The lookalike [In] is not System.Runtime.InteropServices.InAttribute, so the array is copied both ways
        Assert.DoesNotContain("// Skip DtoH copy for [In] array: lookalike\n", wrapper, StringComparison.Ordinal);
        Assert.Contains("// Copy back array: lookalike\n", wrapper, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void Test_DefaultValues_AreWrittenAsCSharpLiterals(string dialect)
    {
        var wrapper = GetWrapper(s_defaultValueKernels, dialect, "DefaultKernels");

        Assert.Contains(
            "LaunchScaleKernel(global::System.Single[] data, global::System.Boolean enabled = true, " +
            "global::System.Boolean inverted = false, global::System.Single scale = 1.5f, " +
            "global::System.Double offset = 0.25d, global::System.Int32 count = 3, " +
            "global::System.String label = \"a\\\"b\", global::System.Single[]? extra = default, ",
            wrapper,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DefaultValues_PointerWrappersTakeNoLiteral()
    {
        var wrapper = GetWrapper(s_defaultValueKernels, "Cuda", "DefaultKernels");

        // The IntPtr wrapper defaults a device pointer to IntPtr.Zero, and the typed wrapper takes an optional buffer
        Assert.Contains("IntPtr data, global::System.Boolean enabled = true, ", wrapper, StringComparison.Ordinal);
        Assert.Contains("IntPtr extra = default, ", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Int32>? extra = null, ", wrapper,
            StringComparison.Ordinal);
        Assert.Contains("extra?.DevicePointer ?? IntPtr.Zero, ", wrapper, StringComparison.Ordinal);
    }

    private static string GetWrapper(string source, string dialect, string className)
    {
        var (_, output, diagnostics) = RoslynTestUtility.RunGenerator(source, dialect);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        return output.SyntaxTrees
            .Single(t => t.FilePath.EndsWith($"{className}.g.cs", StringComparison.Ordinal))
            .ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
