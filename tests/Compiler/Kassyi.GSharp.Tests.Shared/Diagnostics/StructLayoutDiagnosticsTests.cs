using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The struct layout diagnostics: a struct the device cannot lay out (GSH0078), a field type without a device
/// representation (GSH0079), an array converted element by element (GSH0080) and an array whose elements allocate their
/// array fields one by one (GSH0081). Each is reported once by the analyzer at the type of the kernel parameter or
/// local.
/// </summary>
[Category(TestCategory.Compiler)]
public class StructLayoutDiagnosticsTests
{
    private const string TestCase = "Diagnostics/StructLayoutDiagnostics.cs";

    // Overlap by value (line 38) and as an array (line 41), Misaligned by ref (line 44), Named by value (line 47) and
    // Overlap as a local (line 52)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StructWithoutDeviceLayout_ReportsGsh0078AtTheType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 38, 42, 38, 49),
                (Analyzer, 41, 47, 41, 56),
                (Analyzer, 44, 49, 44, 59),
                (Analyzer, 47, 40, 47, 45),
                (Analyzer, 52, 13, 52, 20)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id));
    }

    [Theory]
    [InlineData("Cpu", "'StructLayoutDiagnosticsTest.Overlap'", "field 'B' at offset 4 partially overlaps field 'A'")]
    [InlineData("Cuda", "'StructLayoutDiagnosticsTest.Overlap'", "field 'B' at offset 4 partially overlaps field 'A'")]
    [InlineData(
        "Cpu",
        "'StructLayoutDiagnosticsTest.Misaligned'",
        "field 'B' at offset 1 is not a multiple of its 4-byte device alignment")]
    [InlineData(
        "Cuda",
        "'StructLayoutDiagnosticsTest.Misaligned'",
        "field 'B' at offset 1 is not a multiple of its 4-byte device alignment")]
    [InlineData("Cpu", "'StructLayoutDiagnosticsTest.Named'", "field 'Name' has the reference type 'string'")]
    [InlineData("Cuda", "'StructLayoutDiagnosticsTest.Named'", "field 'Name' has the reference type 'string'")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StructWithoutDeviceLayout_StatesTheReason(string dialect, string structName, string reason)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var messages = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StructLayoutNotRepresentable.Id && d.Message.Contains(structName))
            .Select(static d => d.Message)
            .ToList();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.Contains(reason, message));
    }

    // Priced by value (line 57), and Priced nested in Wrapper (line 98) with the path of the field; GSH0078 does not
    // repeat the field
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FieldWithoutDeviceType_ReportsGsh0079AtTheType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 57, 41, 57, 47)],
            diagnostics.Ranges(GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id, "'decimal' of field 'Price'"));
        Assert.Equal(
            [(Analyzer, 98, 42, 98, 49)],
            diagnostics.Ranges(
                GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id,
                "'decimal' of field 'Inner.Price'"));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id, "Priced"));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id, "Wrapper"));
    }

    // CUDA only: VecHolder[] (line 60) and the inline array of Elem in InlineOuter (line 63), where Vector2 and Float2
    // move to 8-byte offsets; Bag[] (line 66), AutoPair[] (line 101) and the inline array of WithArray in ArraysHolder
    // (line 110), whose host layout the runtime may reorder. The CPU dialect reports GSH0082 instead.
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ArrayOfDivergentStruct_ReportsGsh0080AtTheType(string dialect, bool isCuda)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        IEnumerable<(DiagnosticOrigin, int, int, int, int)> expected = isCuda
            ?
            [
                (Analyzer, 60, 43, 60, 54),
                (Analyzer, 63, 50, 63, 61),
                (Analyzer, 66, 43, 66, 48),
                (Analyzer, 101, 44, 101, 54),
                (Analyzer, 110, 51, 110, 63)
            ]
            : [];
        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.StructArrayConvertedPerElement.Id));
    }

    [Theory]
    [InlineData(
        "Cuda",
        "'StructLayoutDiagnosticsTest.VecHolder'",
        "field 'V' is at offset 4 on the host and 8 on the device")]
    [InlineData(
        "Cuda",
        "'StructLayoutDiagnosticsTest.Elem'",
        "field 'V' is at offset 4 on the host and 8 on the device")]
    [InlineData("Cuda", "'StructLayoutDiagnosticsTest.Bag'", "field 'Data' is an array")]
    [InlineData("Cuda", "'StructLayoutDiagnosticsTest.AutoPair'", "LayoutKind.Auto")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ArrayOfDivergentStruct_StatesTheFirstDifference(string dialect, string structName,
        string difference)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var diagnostic = Assert.Single(
            diagnostics,
            d => d.Id == GSharpDiagnostics.StructArrayConvertedPerElement.Id && d.Message.Contains(structName));
        Assert.Contains(difference, diagnostic.Message);
    }

    // CUDA only: Bag[] (line 66) allocates the Data of each element
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ArrayOfStructWithArrayField_ReportsGsh0081AtTheType(string dialect, bool isCuda)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        IEnumerable<(DiagnosticOrigin, int, int, int, int)> expected = isCuda ? [(Analyzer, 66, 43, 66, 48)] : [];
        Assert.Equal(
            expected,
            diagnostics.Ranges(GSharpDiagnostics.StructArrayFieldAllocatedPerElement.Id, "array field 'Data'"));
    }

    // CPU only, until the CPU launcher converts structs (L4): Bag[] (line 66), AutoPair[] (line 101), Bag by value
    // (line 104) and by ref (line 107), and ArraysHolder by ref (line 110)
    [Theory]
    [InlineData("Cpu", true)]
    [InlineData("Cuda", false)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StructNotIdenticalOnCpu_ReportsGsh0082AtTheType(string dialect, bool isCpu)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        IEnumerable<(DiagnosticOrigin, int, int, int, int)> expected = isCpu
            ?
            [
                (Analyzer, 66, 43, 66, 48),
                (Analyzer, 101, 44, 101, 54),
                (Analyzer, 104, 45, 104, 48),
                (Analyzer, 107, 47, 107, 50),
                (Analyzer, 110, 51, 110, 63)
            ]
            : [];
        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.StructTransferNotSupportedOnCpu.Id));
    }

    [Theory]
    [InlineData("'StructLayoutDiagnosticsTest.Bag'", "field 'Data' is an array")]
    [InlineData("'StructLayoutDiagnosticsTest.AutoPair'", "LayoutKind.Auto")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StructNotIdenticalOnCpu_StatesTheFirstDifference(string structName, string difference)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        var messages = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StructTransferNotSupportedOnCpu.Id && d.Message.Contains(structName))
            .Select(static d => d.Message)
            .ToList();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.Contains(difference, message));
    }

    // A union, arrays of a union and of an identical struct, a divergent struct by value (line 69), adjacent explicit
    // fields (line 95) and structs whose vectors differ only in alignment (line 113) report nothing
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_RepresentableStructs_ReportNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        string[] ids =
        [
            GSharpDiagnostics.StructLayoutNotRepresentable.Id,
            GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id,
            GSharpDiagnostics.StructArrayConvertedPerElement.Id,
            GSharpDiagnostics.StructArrayFieldAllocatedPerElement.Id,
            GSharpDiagnostics.StructTransferNotSupportedOnCpu.Id
        ];
        Assert.DoesNotContain(diagnostics, d => d.StartLine is 69 or 95 or 113 && ids.Contains(d.Id));
    }

    // The generated wrapper has no #error line and compiles without errors of its own
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnsupportedStructs_LeaveGeneratedCodeValid(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(TestCase);

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var generated = run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)).ToList();
        Assert.DoesNotContain(generated, tree => tree.ToString().Contains("#error Unsupported reference type"));
        Assert.DoesNotContain(
            run.Output.GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error &&
                 d.Location.SourceTree is { } tree &&
                 !inputTrees.Contains(tree));
    }
}
