using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

// cuBLAS / cuRAND lazily initialize the process-wide CUDA runtime; concurrent first use races.
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class CuBlasInteropTests
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CublasSgemm_MatchesCpuReference()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        const int M = 64;
        const int N = 48;
        const int K = 32;
        const float Beta = 0.5f;
        var a = CreateMatrix(M * K, seed: 1);
        var b = CreateMatrix(K * N, seed: 2);
        var cInit = CreateMatrix(M * N, seed: 5);
        var c = (float[])cInit.Clone();

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateHandle(out var handle), "cuBLAS library is not available.");

        try
        {
            using var dA = new GpuBuffer<float>(a.Length);
            using var dB = new GpuBuffer<float>(b.Length);
            using var dC = new GpuBuffer<float>(c.Length);
            dA.CopyToDevice(a);
            dB.CopyToDevice(b);
            dC.CopyToDevice(c);

            // Column-major: C(MxN) = 1.0 * A(MxK) * B(KxN) + Beta * C(MxN)
            AssertSuccess(CuBlasApi.cublasSgemm_v2(handle, CuBlasOperation.None, CuBlasOperation.None,
                M, N, K, 1.0f, dA.DevicePointer, M, dB.DevicePointer, K, Beta, dC.DevicePointer, M));
            GpuKernelLoader.SynchronizeDevice();
            dC.CopyToHost(c);
        }
        finally
        {
            AssertSuccess(CuBlasApi.cublasDestroy_v2(handle));
        }

        AssertGemmMatchesProjection(a, b, cInit, c, Beta, M, N, K);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CublasDgemm_TransposedA_MatchesCpuReference()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        const int M = 16;
        const int N = 8;
        const int K = 24;
        var aT = CreateDoubleMatrix(K * M, seed: 3);
        var b = CreateDoubleMatrix(K * N, seed: 4);
        var c = new double[M * N];

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateHandle(out var handle), "cuBLAS library is not available.");

        try
        {
            using var dA = new GpuBuffer<double>(aT.Length);
            using var dB = new GpuBuffer<double>(b.Length);
            using var dC = new GpuBuffer<double>(c.Length);
            dA.CopyToDevice(aT);
            dB.CopyToDevice(b);

            // aT is stored as K x M (column-major); op(A) = aT^T is M x K.
            AssertSuccess(CuBlasApi.cublasDgemm_v2(handle, CuBlasOperation.Transpose, CuBlasOperation.None,
                M, N, K, 2.0, dA.DevicePointer, K, dB.DevicePointer, K, 0.0, dC.DevicePointer, M));
            GpuKernelLoader.SynchronizeDevice();
            dC.CopyToHost(c);
        }
        finally
        {
            AssertSuccess(CuBlasApi.cublasDestroy_v2(handle));
        }

        // Freivalds-style check: verify C·x == 2 * op(A)·(B·x) for a probe vector x.
        // O(MK + KN + MN) instead of the O(MNK) full reference product.
        var x = new double[N];
        for (var col = 0; col < N; col++)
        {
            x[col] = 1.0 + (col % 5) * 0.25;
        }

        var bx = new double[K];
        for (var col = 0; col < N; col++)
        {
            for (var k = 0; k < K; k++)
            {
                bx[k] += b[col * K + k] * x[col];
            }
        }

        for (var row = 0; row < M; row++)
        {
            var expected = 0.0;
            for (var k = 0; k < K; k++)
            {
                expected += aT[row * K + k] * bx[k];
            }

            var actual = 0.0;
            for (var col = 0; col < N; col++)
            {
                actual += c[col * M + row] * x[col];
            }

            Assert.Equal(2.0 * expected, actual, 1e-8);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CublasGetVersion_ReportsSupportedMajorVersion()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateHandle(out var handle), "cuBLAS library is not available.");

        try
        {
            AssertSuccess(CuBlasApi.cublasGetVersion_v2(handle, out var version));
            Assert.True(version >= 120000, $"Unexpected cuBLAS version {version}.");
        }
        finally
        {
            AssertSuccess(CuBlasApi.cublasDestroy_v2(handle));
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CublasSgemm_InvalidLeadingDimension_ReturnsInvalidValue()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateHandle(out var handle), "cuBLAS library is not available.");

        try
        {
            // lda < m is rejected by cuBLAS before any device memory is touched.
            var status = CuBlasApi.cublasSgemm_v2(handle, CuBlasOperation.None, CuBlasOperation.None,
                4, 4, 4, 1.0f, IntPtr.Zero, 1, IntPtr.Zero, 4, 0.0f, IntPtr.Zero, 4);

            Assert.Equal(CuBlasStatus.InvalidValue, status);
        }
        finally
        {
            AssertSuccess(CuBlasApi.cublasDestroy_v2(handle));
        }
    }

    private static bool TryCreateHandle(out IntPtr handle)
    {
        try
        {
            AssertSuccess(CuBlasApi.cublasCreate_v2(out handle));
            return true;
        }
        catch (DllNotFoundException)
        {
            // cuBLAS is an optional CUDA Toolkit component; machines without it cannot run this test.
            handle = IntPtr.Zero;
            return false;
        }
    }

    private static void AssertSuccess(CuBlasStatus status) => Assert.Equal(CuBlasStatus.Success, status);

    private static float[] CreateMatrix(int length, int seed)
    {
        var random = new Random(seed);
        var data = new float[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(random.NextDouble() - 0.5);
        }

        return data;
    }

    private static double[] CreateDoubleMatrix(int length, int seed)
    {
        var random = new Random(seed);
        var data = new double[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = random.NextDouble() - 0.5;
        }

        return data;
    }

    /// <summary>
    /// Freivalds-style check of column-major C(m x n) == A(m x k) * B(k x n) + beta * C0(m x n) using a probe vector.
    /// Cost is O(mk + kn + mn) instead of the O(mnk) full reference product.
    /// </summary>
    private static void AssertGemmMatchesProjection(float[] a, float[] b, float[] cInit, float[] c, float beta, int m, int n, int k)
    {
        var x = new double[n];
        for (var col = 0; col < n; col++)
        {
            x[col] = 1.0 + (col % 5) * 0.25;
        }

        var bx = new double[k];
        for (var col = 0; col < n; col++)
        {
            for (var i = 0; i < k; i++)
            {
                bx[i] += b[col * k + i] * x[col];
            }
        }

        for (var row = 0; row < m; row++)
        {
            var expected = 0.0;
            for (var i = 0; i < k; i++)
            {
                expected += a[i * m + row] * bx[i];
            }

            for (var col = 0; col < n; col++)
            {
                expected += beta * cInit[col * m + row] * x[col];
            }

            var actual = 0.0;
            for (var col = 0; col < n; col++)
            {
                actual += c[col * m + row] * x[col];
            }

            Assert.Equal(expected, actual, 1e-2);
        }
    }
}

