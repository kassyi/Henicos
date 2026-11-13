using System.Globalization;
using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// Which array reads get the null check of #706 (TestCases/Transpiler/Emission/ArrayNullCheckShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    private const string ArrayRef = "__gsharp_array_ref(";
    private const string ArrayAt = "__gsharp_array_at(";

    // A stack array local is never null, so no read of it is checked
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_StackArrayLocal_IsNotChecked(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "ElidedKernel");

        Assert.DoesNotMatch(@"__gsharp_array_(ref|at)\(__gsharp_id_\d+_local\b", kernel);
    }

    // A kernel array parameter may be null whatever its annotation, as in a C# call, so its reads are checked, also
    // when the kernel never writes it
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_UnwrittenKernelParameters_AreChecked(string dialect)
    {
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect), "ElidedKernel");

#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Matches(Regex.Escape(ArrayRef) + @"__gsharp_id_\d+_values, ", kernel);
#pragma warning restore RS0030
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Matches(Regex.Escape(ArrayAt) + @"__gsharp_id_\d+_values, ", kernel);
#pragma warning restore RS0030
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Matches(Regex.Escape(ArrayRef) + @"__gsharp_id_\d+_bias, ", kernel);
#pragma warning restore RS0030
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

#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Equal(6, Regex.Matches(kernel, Regex.Escape(ArrayRef)).Count);
#pragma warning restore RS0030
        // written[0], and the reads and writes of the parameter o, which may be null as well
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Equal(9, Regex.Matches(kernel, Regex.Escape(ArrayAt)).Count);
#pragma warning restore RS0030
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Single(Regex.Matches(kernel, Regex.Escape(ArrayAt + "__gsharp_id_1_written, ")));
#pragma warning restore RS0030
        Assert.Contains(ArrayRef + "__gsharp_id_0_h.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
        Assert.Contains(ArrayAt + "__gsharp_id_1_written, ", kernel, StringComparison.Ordinal);
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Matches(Regex.Escape(ArrayRef) + @"__gsharp_id_\d+_copy, ", kernel);
#pragma warning restore RS0030
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

    // The array launcher writes 0 into the reserved word of every array argument, whose stackalloc'd proxy would
    // otherwise hold garbage under [SkipLocalsInit], and passes an empty array as a non-null one
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayNullCheck_ArrayLauncher_ZeroesTheReservedWordAndPassesEmptyAsNonNull()
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void LaunchElidedKernel(global::", StringComparison.Ordinal);
        Assert.True(start >= 0, "The array launcher of ElidedKernel is not generated.");
        var launcher = LauncherFrom(generated, start);

        foreach (var ordinal in (int[])[0, 1, 2])
        {
            Assert.Contains($"((int*)(__gsharp_proxy_{ordinal} + 1))[1] = 0;", launcher, StringComparison.Ordinal);
        }

        Assert.Contains(
            "__gsharp_proxy_1[0] = global::Kassyi.GSharp.Runtime.Interop.ArrayPinning.IsNull(bias) ? " +
            "global::System.IntPtr.Zero : " +
            "(global::System.IntPtr)global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.EmptyArrayData;",
            launcher,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("NullReadsAsEmpty", launcher, StringComparison.Ordinal);
    }

    // A check in a kernel body reports a site of the kernel's error site table, which holds the place of the receiver, so
    // the host exception names it on CUDA as on the native build (#1409): h.Data in `o[0] = h.Data.Length;` (44,16)
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_KernelBody_ReportsASiteOfTheErrorSiteTable(string dialect)
    {
        var trees = GenerateTrees("ArrayNullCheckShapeKernels", dialect, withPath: true);
        var kernel = KernelSource(Generate("ArrayNullCheckShapeKernels", dialect, withPath: true), "CheckedKernel");

        Assert.DoesNotContain("G#: a null array is read at", kernel, StringComparison.Ordinal);
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        var check = Regex.Match(kernel, Regex.Escape(ArrayRef) + @"__gsharp_id_0_h\.__gsharp_mb_Data, (\d+)u\)\.length");
#pragma warning restore RS0030
        Assert.True(check.Success, "h.Data.Length does not go through a site of the error site table.");
        var siteId = uint.Parse(check.Groups[1].Value, CultureInfo.InvariantCulture);

        // new global::Kassyi.GSharp.Runtime.Aot.GSharpErrorSite(<site>u, <file>, <line>, <column>, <member>, <message>)
        var entry = Assert.Single(
            trees
                .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                .Where(static creation => creation.Type is QualifiedNameSyntax
                {
                    Right.Identifier.ValueText: nameof(GSharpErrorSite)
                }),
            creation => creation.ArgumentList?.Arguments[0].Expression is
                LiteralExpressionSyntax { Token.Value: uint id } && id == siteId);
        var arguments = Assert.IsType<ArgumentListSyntax>(entry.ArgumentList).Arguments;
        object[] expected =
            [siteId, "ArrayNullCheckShapeKernels.cu.cs", 44, 16, "CheckedKernel", "G#: a null array is read"];
        Assert.Equal(
            expected,
            arguments.Select(static argument =>
                Assert.IsType<LiteralExpressionSyntax>(argument.Expression).Token.Value));
    }

    // The Ptr launcher passes IntPtr.Zero as a null array, optional or not, as the array launcher passes null; only a
    // proxy without data that claims elements is rejected
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayNullCheck_PtrLauncher_PassesANullArray()
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void LaunchElidedKernelPtr(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Ptr launcher of ElidedKernel is not generated.");
        var launcher = LauncherFrom(generated, start);

        Assert.Contains(
            "if (values.Data == global::System.IntPtr.Zero && values.Length != 0)",
            launcher,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentNullException", launcher, StringComparison.Ordinal);
    }

    // The nullable annotation is the contract of a device function (#1409): a call checks what it passes to a
    // non-nullable parameter unless it is proven, and the callee reads its parameter unchecked. A field of a struct
    // argument of the kernel may be null, as in a C# call, so the kernel checks it
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ArrayNullCheck_NonNullableAnnotation_IsTheContractOfADeviceFunction(string dialect)
    {
        var generated = Generate("ArrayNullCheckShapeKernels", dialect);
        var kernel = KernelSource(generated, "BoundaryKernel");

        Assert.Contains(ArrayAt + "__gsharp_id_0_p.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Single(Regex.Matches(kernel, Regex.Escape(ArrayRef + "__gsharp_id_1_h.__gsharp_mb_Data, ")));
#pragma warning restore RS0030
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Single(Regex.Matches(kernel, Regex.Escape(ArrayRef + "__gsharp_id_0_p.__gsharp_mb_Data, ")));
#pragma warning restore RS0030
        var structRef = @"__gsharp_struct_ref\(__gsharp_id_\d+_(made|p), [^,]+, " +
            @"\[\]\(const auto& s\) -> decltype\(auto\) \{ return \(s\.__gsharp_mb_Data\); \}\)";
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Equal(2, Regex.Matches(kernel, structRef).Count);
#pragma warning restore RS0030

        var readFirst = generated.AsSpan(generated.LastIndexOf("ReadFirst(GSharpArray", StringComparison.Ordinal));
        readFirst = readFirst[..readFirst.IndexOf('}')];
        Assert.DoesNotContain(ArrayAt, readFirst, StringComparison.Ordinal);
    }

    // The launchers pass a null in a non-nullable array field of a struct argument, as a C# call passes it
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("LaunchBoundaryKernel(global::")]
    [InlineData("LaunchBoundaryKernelPtr(")]
    public void ArrayNullCheck_Launchers_PassANullNonNullableField(string launcherStart)
    {
        var generated = Generate("ArrayNullCheckShapeKernels", "Cuda");
        var start = generated.IndexOf("public static unsafe void " + launcherStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The launcher {launcherStart} is not generated.");
        var launcher = LauncherFrom(generated, start);

        Assert.DoesNotContain("ArgumentNullException", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("p.Data is null", launcher, StringComparison.Ordinal);
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

        // One check per call: the field and the kernel parameter, which may be null as well
#pragma warning disable RS0030 // Category 5: reads the generated C++ of the kernel, which has no syntax tree
        Assert.Equal(2, Regex.Matches(kernel, Regex.Escape("__gsharp_array_not_null(")).Count);
#pragma warning restore RS0030
        Assert.Contains("__gsharp_array_not_null(__gsharp_id_0_h.__gsharp_mb_Data, ", kernel, StringComparison.Ordinal);
        Assert.Contains("__gsharp_array_not_null(__gsharp_id_1_values, ", kernel, StringComparison.Ordinal);
        // The local the call initializes is proven: neither its reads nor the call that passes it are checked
        Assert.DoesNotMatch(@"__gsharp_array_(ref|at)\(__gsharp_id_\d+_data\b", kernel);
        Assert.Matches(@"ReadFirst\(__gsharp_id_\d+_data\)", kernel);
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
