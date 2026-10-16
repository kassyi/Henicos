using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

// cuBLAS / cuRAND lazily initialize the process-wide CUDA runtime; concurrent first use races.
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class CuRandInteropTests
{
    private const int SampleCount = 1 << 16;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CurandGenerateUniform_ProducesValuesInHalfOpenUnitInterval()
    {
        Skip.IfNot(TryGenerateUniform(seed: 1234UL, out var samples), "CUDA driver or cuRAND library is not available.");

        var sum = 0.0;
        foreach (var value in samples)
        {
            // cuRAND uniform distribution excludes 0.0 and includes 1.0.
            Assert.InRange(value, float.Epsilon, 1.0f);
            sum += value;
        }

        Assert.InRange(sum / samples.Length, 0.49, 0.51);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CurandGenerateUniform_SameSeed_IsDeterministic()
    {
        float[] second = [];
        Skip.IfNot(TryGenerateUniform(seed: 42UL, out var first) && TryGenerateUniform(seed: 42UL, out second),
            "CUDA driver or cuRAND library is not available.");

        Assert.Equal(first, second);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CurandGenerateUniform_DifferentSeeds_Diverge()
    {
        float[] second = [];
        Skip.IfNot(TryGenerateUniform(seed: 1UL, out var first) && TryGenerateUniform(seed: 2UL, out second),
            "CUDA driver or cuRAND library is not available.");

        Assert.NotEqual(first, second);

        var divergedCount = 0;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
            {
                divergedCount++;
            }
        }

        Assert.True(divergedCount > first.Length * 99 / 100,
            $"Expected >99% element-wise divergence between different seeds, got {divergedCount}/{first.Length}.");
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CurandGenerateNormal_MatchesRequestedMoments()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateGenerator(out var generator), "cuRAND library is not available.");

        var samples = new float[SampleCount];
        try
        {
            AssertSuccess(CuRandApi.curandSetPseudoRandomGeneratorSeed(generator, 7UL));
            using var buffer = new GpuBuffer<float>(samples.Length);
            AssertSuccess(CuRandApi.curandGenerateNormal(generator, buffer.DevicePointer, (nuint)samples.Length, 3.0f, 0.5f));
            GpuKernelLoader.SynchronizeDevice();
            buffer.CopyToHost(samples);
        }
        finally
        {
            AssertSuccess(CuRandApi.curandDestroyGenerator(generator));
        }

        var mean = 0.0;
        foreach (var value in samples)
        {
            mean += value;
        }

        mean /= samples.Length;
        var variance = 0.0;
        foreach (var value in samples)
        {
            variance += (value - mean) * (value - mean);
        }

        variance /= samples.Length;
        Assert.InRange(mean, 2.98, 3.02);
        Assert.InRange(Math.Sqrt(variance), 0.48, 0.52);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CurandGenerateNormal_OddCount_ReturnsLengthNotMultiple()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        Skip.IfNot(TryCreateGenerator(out var generator), "cuRAND library is not available.");

        try
        {
            using var buffer = new GpuBuffer<float>(3);
            var status = CuRandApi.curandGenerateNormal(generator, buffer.DevicePointer, 3, 0.0f, 1.0f);

            Assert.Equal(CuRandStatus.LengthNotMultiple, status);
        }
        finally
        {
            AssertSuccess(CuRandApi.curandDestroyGenerator(generator));
        }
    }

    private static bool TryGenerateUniform(ulong seed, out float[] samples)
    {
        samples = new float[SampleCount];
        if (!GpuKernelLoader.IsDriverAvailable)
        {
            return false;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        if (!TryCreateGenerator(out var generator))
        {
            return false;
        }

        try
        {
            AssertSuccess(CuRandApi.curandSetPseudoRandomGeneratorSeed(generator, seed));
            using var buffer = new GpuBuffer<float>(samples.Length);
            AssertSuccess(CuRandApi.curandGenerateUniform(generator, buffer.DevicePointer, (nuint)samples.Length));
            GpuKernelLoader.SynchronizeDevice();
            buffer.CopyToHost(samples);
        }
        finally
        {
            AssertSuccess(CuRandApi.curandDestroyGenerator(generator));
        }

        return true;
    }

    private static bool TryCreateGenerator(out IntPtr generator)
    {
        try
        {
            AssertSuccess(CuRandApi.curandCreateGenerator(out generator, CuRandRngType.PseudoDefault));
            return true;
        }
        catch (DllNotFoundException)
        {
            // cuRAND is an optional CUDA Toolkit component; machines without it cannot run this test.
            generator = IntPtr.Zero;
            return false;
        }
    }

    private static void AssertSuccess(CuRandStatus status) => Assert.Equal(CuRandStatus.Success, status);
}
