using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class WmmaTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaInt8RowMajor_Transpilation_EmitsDialectFragmentsAndIntrinsics()
    {
        var (source, diagnostics) = RoslynTestUtility.TranspileMethod<WmmaKernels>(
            nameof(WmmaKernels.WmmaInt8RowMajorKernel), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_matrix_a, 16, 16, 16, signed char, __gsharp_wmma_row_major>", source);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_matrix_b, 16, 16, 16, signed char, __gsharp_wmma_row_major>", source);
        Assert.Contains("__gsharp_wmma_fragment<__gsharp_wmma_accumulator, 16, 16, 16, int, void>", source);
#if GSHARP_CPU
        // The CPU dialect does not implement the operations (GSH0054): each call is a runtime failure
        const string NotImplemented = " is not implemented by the StandardCppDialect dialect\")";
        Assert.Contains("__gsharp_unsupported(\"Wmma.Load" + NotImplemented, source);
        Assert.Contains("__gsharp_unsupported(\"Wmma.Store" + NotImplemented, source);
        Assert.DoesNotContain("__gsharp_wmma_load", source);
#else
        Assert.Contains("__gsharp_wmma_load<__gsharp_wmma_shape<16, 16, 16>, signed char, __gsharp_wmma_row_major>(&", source);
        Assert.Contains("__gsharp_wmma_store<__gsharp_wmma_shape<16, 16, 16>, int>(&", source);
#endif
    }

#if GSHARP_CPU
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void WmmaInt8RowMajor_CpuDialectLaunch_ThrowsNotSupportedException()
    {
        var a = new sbyte[256];
        var b = new sbyte[256];
        var c = new int[256];
        var d = new int[256];

        Assert.Throws<NotSupportedException>(() =>
            WmmaKernels.LaunchWmmaInt8RowMajorKernel(a, b, c, d, blockDimX: 32));
    }
#else
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaInt8_M16N16K16RowMajor_ComputesExactMatrixMultiplyAccumulate()
    {
        var a = new sbyte[16 * 16];
        var b = new sbyte[16 * 16];
        var c = new int[16 * 16];
        var d = new int[16 * 16];

        // A[i, k] = 1 for k == 0, -1 for k == 1, 0 otherwise
        // B[k, j] = i + 1 for k == 0, j + 1 for k == 1 (i.e. B[0, j] = j + 1? Wait: A[i, 0] = i + 1, A[i, 1] = -1; B[0, j] = 1, B[1, j] = j + 1 => (A*B)[i, j] = (i + 1)*1 + (-1)*(j + 1) = i - j)
        for (int i = 0; i < 16; i++)
        {
            a[(i * 16) + 0] = (sbyte)(i + 1);
            a[(i * 16) + 1] = -1;
        }

        for (int j = 0; j < 16; j++)
        {
            b[(0 * 16) + j] = 1;
            b[(1 * 16) + j] = (sbyte)(j + 1);
        }

        // C[i, j] = i when i == j, 0 otherwise
        for (int i = 0; i < 16; i++)
        {
            c[(i * 16) + i] = i;
        }

        WmmaKernels.LaunchWmmaInt8RowMajorKernel(a, b, c, d, blockDimX: 32);

        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                int expected = (i - j) + (i == j ? i : 0);
                Assert.Equal(expected, d[(i * 16) + j]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaInt8_M16N16K16ColMajorB_ComputesExactMatrixMultiplyAccumulate()
    {
        var a = new sbyte[16 * 16];
        var bColMajor = new sbyte[16 * 16];
        var c = new int[16 * 16];
        var d = new int[16 * 16];

        for (int i = 0; i < 16; i++)
        {
            a[(i * 16) + 0] = (sbyte)(i + 1);
            a[(i * 16) + 1] = -1;
        }

        // Column-major B with ldm = 16: element B[k, j] is stored at bColMajor[(j * 16) + k]
        for (int j = 0; j < 16; j++)
        {
            bColMajor[(j * 16) + 0] = 1;
            bColMajor[(j * 16) + 1] = (sbyte)(j + 1);
        }

        for (int i = 0; i < 16; i++)
        {
            c[(i * 16) + i] = i;
        }

        WmmaKernels.LaunchWmmaInt8ColMajorBKernel(a, bColMajor, c, d, blockDimX: 32);

        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                int expected = (i - j) + (i == j ? i : 0);
                Assert.Equal(expected, d[(i * 16) + j]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaInt8_M16N16K16ColMajorStore_WritesTransposedMemoryLayout()
    {
        var a = new sbyte[16 * 16];
        var b = new sbyte[16 * 16];
        var dColMajor = new int[16 * 16];

        for (int i = 0; i < 16; i++)
        {
            a[(i * 16) + 0] = (sbyte)(i + 1);
            a[(i * 16) + 1] = -1;
        }

        for (int j = 0; j < 16; j++)
        {
            b[(0 * 16) + j] = 1;
            b[(1 * 16) + j] = (sbyte)(j + 1);
        }

        WmmaKernels.LaunchWmmaInt8ColMajorStoreKernel(a, b, dColMajor, blockDimX: 32);

        // Column-major store with ldm = 16 places logical D[i, j] = i - j at dColMajor[(j * 16) + i]
        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                int expected = i - j;
                Assert.Equal(expected, dColMajor[(j * 16) + i]);
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WmmaDouble_M8N8K4RowMajor_ComputesExactMatrixMultiplyAccumulate()
    {
        var a = new double[8 * 4];
        var b = new double[4 * 8];
        var d = new double[8 * 8];

        // A[i, k] = 1.0 for all i in 0..7, k in 0..3
        for (int i = 0; i < 8 * 4; i++)
        {
            a[i] = 1.0;
        }

        // B[k, j] = j + 1 for all k in 0..3, j in 0..7
        for (int k = 0; k < 4; k++)
        {
            for (int j = 0; j < 8; j++)
            {
                b[(k * 8) + j] = j + 1;
            }
        }

        WmmaKernels.LaunchWmmaDoubleM8N8K4Kernel(a, b, d, blockDimX: 32);

        // D[i, j] = sum_{k=0..3} 1.0 * (j + 1) = 4.0 * (j + 1)
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                Assert.Equal(4.0 * (j + 1), d[(i * 8) + j]);
            }
        }
    }
#endif
}
