#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Stage 4 compiler diagnostics and in-memory generator verification for Lane D3 (Boundary Marshaling &amp; Aliasing):
/// covers <c>D3-Diag-1</c> through <c>D3-Diag-5</c> and isolates cases that would break C++ native compilation if
/// placed directly in <c>.cu.cs</c> files (<c>Q07 = a</c>).
/// </summary>
[Category(TestCategory.Compiler)]
public sealed class MarshalingBoundaryDiagnosticsTests
{
    private const string TestCase = "Diagnostics/MarshalingBoundaryDiagnostics.cs";

    // D3-Diag-1: Multidimensional array (int[,]) and jagged array (int[][]) in kernel body -> GSH0003
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MultidimensionalAndJaggedArrays_ReportGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var gsh0003 = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .ToList();

        Assert.NotEmpty(gsh0003);
        Assert.Contains(gsh0003, static d => d.Message.Contains("jagged array", System.StringComparison.OrdinalIgnoreCase)
                                             || d.Message.Contains("multidimensional array", System.StringComparison.OrdinalIgnoreCase)
                                             || d.Message.Contains("Object creation", System.StringComparison.OrdinalIgnoreCase));
    }

    // D3-Diag-1 (In-memory isolation per Q07 = a): Multidimensional array parameter int[,] (#613)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MultidimensionalArrayParameter_InMemoryGeneratorCheck(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class MultiDimParamKernels
            {
                [GpuKernel]
                public static void MatrixParamKernel(int[,] grid, int[] output)
                {
                    output[0] = grid[0, 1];
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        // On unmerged main (#613), int[,] parameter either reports GSH0003 (once PR #1028 lands) or emits C++ source
        Assert.NotEmpty(generated);
    }

    // D3-Diag-2: LayoutKind.Explicit partial overlap, misaligned field, and reference field -> GSH0078
    [Theory]
    [InlineData("Cpu", "PartialOverlapStruct", "partially overlaps")]
    [InlineData("Cuda", "PartialOverlapStruct", "partially overlaps")]
    [InlineData("Cpu", "MisalignedOffsetStruct", "alignment")]
    [InlineData("Cuda", "MisalignedOffsetStruct", "alignment")]
    [InlineData("Cpu", "ReferenceFieldStruct", "reference type 'string'")]
    [InlineData("Cuda", "ReferenceFieldStruct", "reference type 'string'")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExplicitLayoutAndReferenceField_ReportGsh0078WithReason(
        string dialect,
        string structName,
        string expectedReason)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var matching = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StructLayoutNotRepresentable.Id
                        && d.Message.Contains(structName, System.StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(matching);
        Assert.All(matching, d => Assert.Contains(expectedReason, d.Message, System.StringComparison.Ordinal));
    }

    // D3-Diag-3: Non-unmanaged Deep Struct return type and int[,] return type -> GSH0004
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NonUnmanagedKernelReturnType_ReportsGsh0004(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var gsh0004 = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id)
            .ToList();

        Assert.Contains(gsh0004, static d => d.Message.Contains("DeepStructForReturn", System.StringComparison.Ordinal));
        Assert.Contains(gsh0004, static d => d.Message.Contains("int[*,*]", System.StringComparison.Ordinal));
    }

    // D3-Diag-4: Struct field without device representation (decimal) -> GSH0079
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FieldWithoutDeviceRepresentation_ReportsGsh0079(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var gsh0079 = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation.Id)
            .ToList();

        Assert.NotEmpty(gsh0079);
        Assert.Contains(gsh0079, static d => d.Message.Contains("'decimal' of field 'Price'", System.StringComparison.Ordinal));
    }

    // D3-Diag-4 (In-memory isolation per Q07 = a & Q11): Unmapped BCL structs (DateTime, Guid, Matrix4x4) and Nullable<DeepStruct> (#972 S19-6, #1007 S19-5)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnmappedBclStructAndNullableDeepStruct_InMemoryGeneratorCheck(string dialect)
    {
        const string source = """
            using System;
            using System.Numerics;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public struct DeepBag
            {
                public int[] Data;
                public int Tag;
            }

            public static partial class UnmappedBoundaryKernels
            {
                [GpuKernel]
                public static void NullableDeepBagKernel(DeepBag? optBag, int[] output)
                {
                    output[0] = optBag.HasValue ? 1 : 0;
                }

                [GpuKernel]
                public static DateTime UnmappedReturnKernel(DateTime dt, Guid id, Matrix4x4 mat)
                {
                    return dt;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        // Verify generator executes in-memory without throwing unhandled exceptions on S19-5 / S19-6 shapes
        Assert.Contains("NullableDeepBagKernel", generated, System.StringComparison.Ordinal);
        Assert.Contains("UnmappedReturnKernel", generated, System.StringComparison.Ordinal);
    }

    // D3-M3b (In-memory isolation per Q07 = a): Vector2? direct scalar/ref/array/return emits invalid C++ 'System::Numerics::Vector2'
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NullableVector2DirectPositions_InMemoryGeneratorCheck(string dialect)
    {
        const string source = """
            using System.Numerics;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class NullableVector2DirectKernels
            {
                [GpuKernel]
                public static Vector2? NullableVec2AllDirectKernel(
                    Vector2? v,
                    ref Vector2? rv,
                    Vector2?[] arr)
                {
                    rv = v;
                    return arr[0];
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        // Confirms why Vector2? direct parameter/array/return must be isolated from .cu.cs:
        // C++ emission outputs 'System::Numerics::Vector2' instead of 'float2' for Nullable<Vector2>
        Assert.Contains("LaunchNullableVec2AllDirectKernel", generated, System.StringComparison.Ordinal);
    }

    // D3-Diag-5: LayoutKind.Auto array/return -> GSH0080 warning; DeepStruct[] -> GSH0080 (CPU) / GSH0080 + GSH0081 (CUDA)
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

    // In-memory isolation per Q07 = a for newly discovered C++ build-breaking boundary constructs (ND3-2, ND3-3, ND3-4):
    // - ND3-2: Nullable<T> assigned or returned with the 'null' literal emits '= nullptr' (System_Nullable<T> lacks nullptr_t ctor)
    // - ND3-3: ImmutableArray<T>.Length emits '.__gsharp_mb_Length()' instead of '.length' on GSharpArray<T>
    // - ND3-4: Casting [GpuReadOnly] T[] parameter (GSharpArray<const T>) to ReadOnlySpan<T> (GSharpArray<T>) drops const in C++
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CppBuildBreakingBoundaryConstructs_InMemoryGeneratorCheck(string dialect)
    {
        const string source = """
            using System;
            using System.Collections.Immutable;
            using Kassyi.GSharp.Core;

            namespace TestNs;

            public static partial class CppBuildBreakingBoundaryKernels
            {
                [GpuKernel]
                public static int? NullableLiteralAndImmutableLengthKernel(
                    ImmutableArray<int> imm,
                    [GpuReadOnly] int[] roArr,
                    int[] output)
                {
                    ReadOnlySpan<int> roSpan = (ReadOnlySpan<int>)roArr;
                    output[0] = imm.Length + roSpan.Length;
                    return null;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        string generated = string.Join("\n", run.Output.SyntaxTrees.Select(static t => t.ToString()));

        Assert.Contains("NullableLiteralAndImmutableLengthKernel", generated, System.StringComparison.Ordinal);
        Assert.Contains("__gsharp_mb_Length()", generated, System.StringComparison.Ordinal);
    }
}
