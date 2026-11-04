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

    // Multidimensional array (int[,]) in kernel body -> GSH0003
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MultidimensionalArrayLocal_ReportsGsh0003(string dialect)
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
    public async Task Test_MultidimensionalArrayParameter_ReportsGsh0003(string dialect)
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
    public async Task Test_ExplicitLayoutAndReferenceField_ReportGsh0078WithReason(
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
    public async Task Test_NonUnmanagedKernelReturnType_ReportsGsh0004(string dialect)
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
    public async Task Test_FieldWithoutDeviceRepresentation_ReportsGsh0079(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 88, 49, 88, 67)],
            diagnostics.Ranges(GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id));
    }

    // Nullable<DeepStruct> parameter where struct has an array field (#972) -> GSH0078
    [Theory(Skip = "Nullable<DeepStruct> parameter is not diagnosed (#972)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NullableDeepStructParameter_ReportsGsh0078(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct DeepBag
            {
                public int[] Data;
                public int Tag;
            }

            public static partial class NullableDeepStructKernels
            {
                [GpuKernel]
                public static void NullableDeepBagKernel(DeepBag? optBag, int[] output)
                {
                    output[0] = optBag.HasValue ? 1 : 0;
                }
            }
            """;

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 15, 46, 15, 61)],
            diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id));
    }

    // Returning unmapped unmanaged BCL struct (DateTime, Guid, Matrix4x4) (#1007)
    [Theory(Skip = "Returning unmapped BCL struct is not diagnosed (#1007)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnmappedBclStructReturn_ReportsDiagnostic(string dialect)
    {
        const string source = """
            using System;
            using System.Numerics;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class UnmappedBclReturnKernels
            {
                [GpuKernel]
                public static DateTime UnmappedReturnKernel(DateTime dt, Guid id, Matrix4x4 mat)
                {
                    return dt;
                }
            }
            """;

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 10, 28, 10, 48)],
            diagnostics.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));
    }

    // Vector2? in four positions (argument, ref parameter, array, and return value) emits System_Nullable<float2>
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_NullableVector2DirectPositions_EmitsValidTypes(string dialect)
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

    // LayoutKind.Auto array/return -> GSH0080 warning; DeepStruct[] -> GSH0080 (CPU) / GSH0080 + GSH0081 (CUDA)
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_AutoLayoutAndDeepStructArrays_ReportGsh0080AndDialectSpecificGsh0081(
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
    [Theory(Skip = "ImmutableArray<T>.Length emits undefined __gsharp_mb_Length() (#1070)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ImmutableArrayLength_EmitsLengthMember(string dialect)
    {
        const string source = """
            using System.Collections.Immutable;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class ImmutableLengthKernels
            {
                [GpuKernel]
                public static void ImmutableLengthKernel(ImmutableArray<int> imm, int[] output)
                {
                    output[0] = imm.Length;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("__gsharp_mb_Length()", generated, System.StringComparison.Ordinal);
        Assert.Contains("__gsharp_id_1_output[0]=__gsharp_id_0_imm.length;", generated, System.StringComparison.Ordinal);
    }

    // Casting [GpuReadOnly] T[] parameter to ReadOnlySpan<T> must preserve const in C++ (#1071)
    [Theory(Skip = "Converting [GpuReadOnly] array to ReadOnlySpan drops const (#1071)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ReadOnlyArrayToReadOnlySpan_PreservesConst(string dialect)
    {
        const string source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class ReadOnlyArrayToSpanKernels
            {
                [GpuKernel]
                public static void ReadOnlyArrayToSpanKernel([GpuReadOnly] int[] roArr, int[] output)
                {
                    ReadOnlySpan<int> roSpan = (ReadOnlySpan<int>)roArr;
                    output[0] = roSpan.Length;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("GSharpArray<int> __gsharp_id_2_roSpan", generated, System.StringComparison.Ordinal);
        Assert.Contains("GSharpArray<const int> __gsharp_id_2_roSpan", generated, System.StringComparison.Ordinal);
    }

    // Returning null literal from Nullable<T> kernel (#984)
    [Theory(Skip = "Nullable<T> return null emits nullptr incompatible with System_Nullable (#984)")]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NullableReturnNull_EmitsValidNullableReturn(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class NullableReturnNullKernels
            {
                [GpuKernel]
                public static int? ReturnNullKernel(int[] output)
                {
                    output[0] = 0;
                    return null;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.DoesNotContain("= nullptr", generated, System.StringComparison.Ordinal);
    }
}
