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

    // A check in a kernel body reports a site of the kernel's error site table, which holds the place of the receiver, so
    // the host exception names it on CUDA as on the native build (#1409): h.Data in `o[0] = h.Data.Length;` (44,16)
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_KernelBody_ReportsASiteOfTheErrorSiteTable(string dialect)
    {
        var generated = Generate("ArrayNullCheckShapeKernels", dialect, withPath: true);
        var kernel = KernelSource(generated, "CheckedKernel");

        Assert.DoesNotContain("G#: a null array is read at", kernel, StringComparison.Ordinal);
        var check = Regex.Match(kernel, Regex.Escape(ArrayRef) + @"__gsharp_id_0_h\.__gsharp_mb_Data, (\d+)u\)\.length");
        Assert.True(check.Success, "h.Data.Length does not go through a site of the error site table.");
        var entry = Regex.Match(generated, @"new global::Kassyi\.GSharp\.Runtime\.Aot\.GSharpErrorSite\(" + check.Groups[1].Value + @"u, [^\n]*");
        Assert.True(entry.Success, "The error site table has no entry for the site of h.Data.Length.");
        Assert.True(
            entry.Value.EndsWith(
                "u, \"ArrayNullCheckShapeKernels.cu.cs\", 44, 16, \"CheckedKernel\", \"G#: a null array is read\"),",
                StringComparison.Ordinal),
            entry.Value);
    }

    // The Ptr launcher rejects IntPtr.Zero for an array that is not optional, as the array launcher rejects null: the
    // kernel leaves out the check of an array parameter it never writes on that promise. An optional one is not rejected
    // and reads as empty (#1409)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayNullCheck_PtrLauncher_RejectsANullArrayThatIsNotOptional()
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void LaunchElidedKernelPtr(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Ptr launcher of ElidedKernel is not generated.");
        var launcher = generated[start..generated.IndexOf("#pragma warning restore CS8500", start, StringComparison.Ordinal)];

        Assert.Contains("if (values.Data == global::System.IntPtr.Zero)", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "throw new global::System.ArgumentNullException(\"values\");",
            launcher,
            StringComparison.Ordinal);
        Assert.Contains("throw new global::System.ArgumentNullException(\"o\");", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentNullException(\"bias\")", launcher, StringComparison.Ordinal);
    }

    // The nullable annotation is the contract (#1409): a non-nullable field of a struct argument is read unchecked, a call
    // checks what it passes to a non-nullable parameter unless it is proven, and the callee reads its parameter unchecked
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_NonNullableAnnotation_IsCheckedAtKernelEntryOnly(string dialect)
    {
        var generated = Generate("ArrayNullCheckShapeKernels", dialect);
        var kernel = KernelSource(generated, "BoundaryKernel");

        Assert.DoesNotContain(ArrayAt, kernel, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(kernel, Regex.Escape(ArrayRef + "__gsharp_id_1_h.__gsharp_mb_Data, ")));
        Assert.Contains("ReadFirst(__gsharp_id_0_p.__gsharp_mb_Data)", kernel, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(kernel, @"__gsharp_struct_ref\(__gsharp_id_\d+_made, [^,]+, \[\]\(const auto& s\) -> decltype\(auto\) \{ return \(s\.__gsharp_mb_Data\); \}\)"));
        Assert.Contains("ReadHolder(__gsharp_id_0_p)", kernel, StringComparison.Ordinal);

        var readFirst = generated[generated.LastIndexOf("ReadFirst(GSharpArray", StringComparison.Ordinal)..];
        readFirst = readFirst[..readFirst.IndexOf('}', StringComparison.Ordinal)];
        Assert.DoesNotContain(ArrayAt, readFirst, StringComparison.Ordinal);
    }

    // The launchers enforce the contract of a non-nullable array field of a struct argument; a nullable one is not
    // checked (#1409)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("LaunchBoundaryKernel(global::", "p.Data is null")]
    [InlineData("LaunchBoundaryKernelPtr(", "p.Data.Data == global::System.IntPtr.Zero")]
    public void ArrayNullCheck_Launchers_RejectANullNonNullableField(string launcherStart, string test)
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void " + launcherStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The launcher {launcherStart} is not generated.");
        var launcher = generated[start..generated.IndexOf("#pragma warning restore CS8500", start, StringComparison.Ordinal)];

        Assert.Contains($"if ({test})", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "throw new global::System.ArgumentNullException(\"p\", " +
            "\"p.Data is null, but the field is not nullable.\");",
            launcher,
            StringComparison.Ordinal);
        Assert.DoesNotContain("h.Data", launcher, StringComparison.Ordinal);
    }

    // A call through a delegate could not check the argument that the callee reads unchecked (#1409)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task ArrayNullCheck_MethodGroupOfACheckedDeviceFunction_IsGsh0003(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/Emission/NonNullableDelegateKernels.cu.cs"));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var generated = string.Join("\n", run.Output.SyntaxTrees.Where(t => !run.Input.SyntaxTrees.Contains(t)).Select(static t => t.ToString()));
        Assert.Contains(
            "(14,40): GSH0003: The syntax 'a [GpuDeviceFunction] with a non-nullable array parameter (or a struct parameter that holds one) passed as a delegate' is not supported",
            generated,
            StringComparison.Ordinal);
    }

    // Gpu.NotNull checks the field once, at the call; the local it initializes is proven, so its reads and the call
    // that passes it are not checked, and Gpu.NotNull of a proven array writes the array alone
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_GpuNotNull_ChecksOnceAndProvesTheLocal(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "NotNullKernel");

        // Exactly one check: the first occurrence is also the last
        var first = kernel.IndexOf("__gsharp_array_not_null(", StringComparison.Ordinal);
        Assert.True(first >= 0, "Gpu.NotNull of the field is not checked.");
        Assert.Equal(first, kernel.LastIndexOf("__gsharp_array_not_null(", StringComparison.Ordinal));
        Assert.Contains("__gsharp_array_not_null(__gsharp_id_0_h.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrayRef, kernel, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrayAt, kernel, StringComparison.Ordinal);
        Assert.Matches(@"ReadFirst\(__gsharp_id_\d+_data\)", kernel);
        Assert.Matches(@"\(__gsharp_id_1_values\)\.length", kernel);
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
