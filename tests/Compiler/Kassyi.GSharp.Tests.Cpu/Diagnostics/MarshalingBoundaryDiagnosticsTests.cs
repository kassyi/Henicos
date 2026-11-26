#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Compiler diagnostics and in-memory generator verification for boundary marshaling and aliasing.
/// </summary>
[Category(TestCategory.Compiler)]
public sealed class MarshalingBoundaryDiagnosticsTests
{
    private const string TestCase = "Diagnostics/MarshalingBoundaryDiagnostics.cs";
    private const string MultiDimParamTestCase = "Diagnostics/MultidimensionalParameterDiagnostics.cs";
    private const string NullableVector2DirectTestCase = "Diagnostics/NullableVector2DirectDiagnostics.cs";
    private const string NullableStructValueTestCase = "Diagnostics/NullableStructValueDiagnostics.cs";
    private const string NullableDeepStructTestCase = "Diagnostics/NullableDeepStructDiagnostics.cs";
    private const string UnmappedBclReturnTestCase = "Diagnostics/UnmappedBclReturnDiagnostics.cs";
    private const string ImmutableArrayLengthTestCase = "Diagnostics/ImmutableArrayLengthDiagnostics.cs";
    private const string NullableReturnNullTestCase = "Diagnostics/NullableReturnNullDiagnostics.cs";

    // Multidimensional array (int[,]) in kernel body -> GSH0003
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task MultidimensionalArrayLocal_LocalDeclaration_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 58, 25, 58, 38)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, GpuCodeConstraints.MultidimensionalArray));
    }

    // Multidimensional array parameter int[,] (#613) -> GSH0003
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task MultidimensionalArrayParameter_ParameterDeclaration_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(MultiDimParamTestCase, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 9, 42, 9, 48),
                (DiagnosticOrigin.Analyzer, 11, 21, 11, 31)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, GpuCodeConstraints.MultidimensionalArray));
    }

    // LayoutKind.Explicit partial overlap, misaligned field, and reference field -> GSH0078
    [Theory]
    [InlineData("Cpu", "PartialOverlapStruct", "partially overlaps", 65, 9, 65, 29)]
    [InlineData("Cuda", "PartialOverlapStruct", "partially overlaps", 65, 9, 65, 29)]
    [InlineData("Cpu", "MisalignedOffsetStruct", "alignment", 66, 9, 66, 31)]
    [InlineData("Cuda", "MisalignedOffsetStruct", "alignment", 66, 9, 66, 31)]
    [InlineData("Cpu", "ReferenceFieldStruct", "reference type 'string'", 67, 9, 67, 29)]
    [InlineData("Cuda", "ReferenceFieldStruct", "reference type 'string'", 67, 9, 67, 29)]
    [Scenario(TestScenario.SadPath)]
    public async Task ExplicitLayoutAndReferenceField_StructDeclaration_ReportsGsh0078WithReason(
        string dialect,
        string structName,
        string expectedReason,
        int startLine,
        int startCol,
        int endLine,
        int endCol)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, startLine, startCol, endLine, endCol)],
            diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id, structName));

        var matching = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StructLayoutNotRepresentable.Id
                        && d.Message.Contains(structName, System.StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(matching);
        Assert.All(matching, d => Assert.Contains(expectedReason, d.Message, System.StringComparison.Ordinal));
    }

    // Non-unmanaged Deep Struct return type and int[,] return type -> GSH0004
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NonUnmanagedKernelReturnType_ManagedReturnType_ReportsGsh0004(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 73, 39, 73, 61),
                (DiagnosticOrigin.Analyzer, 79, 26, 79, 54)
            ],
            diagnostics.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));
    }

    // Struct field without device representation (decimal) -> GSH0079
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task FieldWithoutDeviceRepresentation_UnsupportedFieldType_ReportsGsh0079(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 88, 49, 88, 67)],
            diagnostics.Ranges(GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id));
    }

    // Nullable<DeepStruct> parameter where the struct has an array field (#972): converted through the shadow of its
    // Nullable, whose Marshal_ marshals the value's array, so nothing is reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task NullableDeepStructParameter_NullableComplexStruct_ConvertsWithoutDiagnostic(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(NullableDeepStructTestCase);

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);
        var generated = string.Join('|', run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain(run.Analyzer, static d => d.Id == GSharpDiagnostics.StructLayoutNotRepresentable.Id);
        Assert.DoesNotContain(run.Generator, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "Marshal___gsharp_nullable_of_DeepBag(ref optBag, ref ", generated, System.StringComparison.Ordinal);
        Assert.Contains("Marshal_DeepBag(ref value, ref native.Value, ", generated, System.StringComparison.Ordinal);
    }

    // Returning an unmapped BCL struct (DateTime) reports GSH0004 at the kernel name (regression test for #1007)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnmappedBclStructReturn_UnmappedType_ReportsDiagnostic(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(UnmappedBclReturnTestCase, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 10, 28, 10, 48)],
            diagnostics.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));
    }

    // Vector2? in four positions (argument, ref parameter, array, and return value) emits System_Nullable<float2>
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task NullableVector2DirectPositions_GeneratorRun_EmitsValidTypes(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(NullableVector2DirectTestCase));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("System::Numerics::Vector2", generated, System.StringComparison.Ordinal);
        Assert.Contains("System_Nullable<float2> __gsharp_id_0_v", generated, System.StringComparison.Ordinal);
        Assert.Contains("System_Nullable<float2>* __gsharp_id_1_rv", generated, System.StringComparison.Ordinal);
        Assert.Contains("GSharpArray<System_Nullable<float2>> __gsharp_id_2_arr", generated, System.StringComparison.Ordinal);
        Assert.Contains("System_Nullable<float2>* __restrict__ __gsharp_auto_result", generated, System.StringComparison.Ordinal);
    }

    // A Vector2? array and return value are converted element by element on CUDA (#961), where the value is at 8
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.Boundary)]
    public async Task NullableVector2ArrayAndReturn_ValueAlignedDifferently_ReportsGsh0080OnCuda(
        string dialect,
        bool converted)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(NullableVector2DirectTestCase, dialect);

        (DiagnosticOrigin, int, int, int, int)[] expected = converted
            ? [(DiagnosticOrigin.Analyzer, 10, 19, 10, 27), (DiagnosticOrigin.Analyzer, 13, 9, 13, 19)]
            : [];
        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.StructArrayConvertedPerElement.Id));
        Assert.All(
            diagnostics.Where(static d => d.Id == GSharpDiagnostics.StructArrayConvertedPerElement.Id),
            static d => Assert.Contains(
                "'System.Numerics.Vector2?' are converted element by element because its host and device layouts " +
                "differ (the value is at offset 4 on the host and 8 on the device)",
                d.Message,
                System.StringComparison.Ordinal));
    }

    // The value of a Nullable<S> parameter is checked like S: a reference field is GSH0078 (#972)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NullableStructParameter_ValueWithReferenceField_ReportsGsh0078(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(NullableStructValueTestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 14, 53, 14, 72)],
            diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id, "NullableNameHolder"));
    }

    // LayoutKind.Auto array/return -> GSH0080 warning; DeepStruct[] -> GSH0080 (CPU) / GSH0080 + GSH0081 (CUDA)
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.Boundary)]
    public async Task AutoLayoutAndDeepStructArrays_ComplexArrayFields_ReportsGsh0080AndGsh0081(
        string dialect,
        bool expectGsh0081)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var gsh0080 = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.StructArrayConvertedPerElement.Id)
            .ToList();
        var gsh0081 = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.StructArrayFieldAllocatedPerElement.Id)
            .ToList();

        // AutoLayoutBoundaryStruct return + AutoLayoutBoundaryStruct[] + DeepStructForReturn[] -> 3 GSH0080 warnings
        Assert.Equal(3, gsh0080.Count);
        Assert.Equal(expectGsh0081 ? 1 : 0, gsh0081.Count);
    }

    // ImmutableArray<T>.Length emits .length instead of undefined __gsharp_mb_Length() (#1070)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ImmutableArrayLength_GeneratorRun_EmitsLengthMember(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(ImmutableArrayLengthTestCase);

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("__gsharp_mb_Length()", generated, System.StringComparison.Ordinal);
        // A default ImmutableArray<T> has no array, so its Length throws as in C#: the read goes through the null check
        Assert.Contains("=__gsharp_array_ref(__gsharp_id_0_imm, ", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=__gsharp_id_0_imm.length;", generated, System.StringComparison.Ordinal);
    }

    // Returning null literal from Nullable<T> kernel (#984)
    [Theory(Skip = "Nullable<T> return null emits nullptr incompatible with System_Nullable (#984)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NullableReturnNull_GeneratorRun_EmitsValidNullableReturn(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(NullableReturnNullTestCase);

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("= nullptr", generated, System.StringComparison.Ordinal);
    }
}
