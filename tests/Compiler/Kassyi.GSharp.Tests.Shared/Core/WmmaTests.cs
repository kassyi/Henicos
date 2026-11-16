using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public partial class WmmaTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaInt8RowMajor_Transpilation_EmitsDialectFragmentsAndIntrinsics()
    {
        var (source, diagnostics) = TranspileTestRunner.TranspileMethod<WmmaKernels>(
            nameof(WmmaKernels.WmmaInt8RowMajorKernel), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_matrix_a, 16, 16, 16, signed char, __gsharp_wmma_row_major>", source);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_matrix_b, 16, 16, 16, signed char, __gsharp_wmma_row_major>", source);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_accumulator, 16, 16, 16, int, void>", source);
        AssertWmmaDialectSource(source);
    }

    partial void AssertWmmaDialectSource(string source);
}
