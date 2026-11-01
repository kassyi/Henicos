#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Verifies that vectorized <c>GpuMath</c> expressions reject evaluation under <see cref="GpuKernelLoader.IsCpuFallbackMode"/>
/// with <see cref="NotSupportedException"/> instead of passing <c>Marshal.AllocHGlobal</c> host pointers to
/// CUDA kernels, while host-known <c>Count</c> continues to succeed and an active CUDA context stays healthy.
/// </summary>
[Category(TestCategory.Unit)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class VectorCpuFallbackTests
{
    private const int Words = 2;

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Evaluate_UnderCpuFallback_ThrowsNotSupportedException()
    {
        IDisposable? outerContextScope = GpuKernelLoader.IsDriverAvailable
            ? new GpuKernelLoader.CudaContextScope()
            : null;
        try
        {
            GpuKernelLoader.ForceCpuFallback = true;
            try
            {
                using var ws = new GpuWorkspace(64);
                using var array = CreateIntArray(ws, [1, 2, 3, 4]);
                using var emptyArray = CreateIntArray(ws, []);
                using var zeroMag = GpuBigUInt.FromBigInteger(ws, 0, Words);
                using var oneMag = GpuBigUInt.FromBigInteger(ws, 1, Words);
                var zero = new GpuBigInt(zeroMag, false);
                var scalar = new GpuBigInt(oneMag, false);

                var v = array.AsVectorizedEnumerable();
                var emptyV = emptyArray.AsVectorizedEnumerable();

                Assert.Throws<NotSupportedException>(() => v.Sum().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Max().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Min().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Where(v.IsGreaterThan(zero)).Count().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.ZipAdd(v).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.SelectAdd(scalar).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.IsGreaterThan(zero).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.IsGreaterThan(zero).Any().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.IsGreaterThan(zero).All().Evaluate(ws));

                Assert.Throws<NotSupportedException>(() => emptyV.Sum().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => emptyV.Max().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => emptyV.Min().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => emptyV.IsGreaterThan(zero).Any().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => emptyV.IsGreaterThan(zero).All().Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Take(0).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => v.Skip(4).Evaluate(ws));

                using var floatArray = CreateFloatArray(ws, [1, 2, 3, 4]);
                using var emptyFloatArray = CreateFloatArray(ws, []);
                using var floatDest = new GpuBigFloatArray(ws, 4, Words);
                var fZero = new GpuBigFloat(zeroMag, 0, false);
                var fScalar = new GpuBigFloat(oneMag, 0, false);

                var fv = floatArray.AsVectorizedEnumerable();
                var emptyFv = emptyFloatArray.AsVectorizedEnumerable();

                Assert.Throws<NotSupportedException>(() => fv.Sum(ws));
                Assert.Throws<NotSupportedException>(() => fv.Max(ws));
                Assert.Throws<NotSupportedException>(() => fv.Min(ws));
                Assert.Throws<NotSupportedException>(() => fv.Average(ws));
                Assert.Throws<NotSupportedException>(() => fv.SelectAdd(fScalar).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => fv.Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => fv.EvaluateTo(ws, floatDest));
                Assert.Throws<NotSupportedException>(() => fv.Where(fv.IsGreaterThan(fZero)).EvaluateTo(ws, floatDest));
                Assert.Throws<NotSupportedException>(() => fv.Where(fv.IsGreaterThan(fZero)).Count(ws));

                Assert.Throws<NotSupportedException>(() => emptyFv.Sum(ws));
                Assert.Throws<NotSupportedException>(() => emptyFv.Max(ws));
                Assert.Throws<NotSupportedException>(() => emptyFv.Min(ws));
                Assert.Throws<NotSupportedException>(() => emptyFv.Average(ws));
                Assert.Throws<NotSupportedException>(() => fv.Take(0).Evaluate(ws));
                Assert.Throws<NotSupportedException>(() => fv.Skip(4).Evaluate(ws));
            }
            finally
            {
                GpuKernelLoader.ForceCpuFallback = false;
            }
        }
        finally
        {
            outerContextScope?.Dispose();
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void HostKnownCount_And_SubsequentGpuExecution_SucceedAfterCpuFallbackRejection()
    {
        CudaCapability.EnsureGpuAvailable();
        using var outerScope = new GpuKernelLoader.CudaContextScope();

        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var ws = new GpuWorkspace(64);
            using var array = CreateIntArray(ws, [1, 2, 3, 4]);
            using var floatArray = CreateFloatArray(ws, [1, 2, 3, 4]);
            using var oneMag = GpuBigUInt.FromBigInteger(ws, 1, Words);
            var scalar = new GpuBigInt(oneMag, false);
            var fScalar = new GpuBigFloat(oneMag, 0, false);

            var v = array.AsVectorizedEnumerable();
            var fv = floatArray.AsVectorizedEnumerable();

            using var vCount = v.Count().Evaluate(ws);
            using var vSelectCount = v.SelectAdd(scalar).Count().Evaluate(ws);

            Assert.Equal(new BigInteger(4), vCount.Value.ToBigInteger(vCount.IsNegative));
            Assert.Equal(new BigInteger(4), vSelectCount.Value.ToBigInteger(vSelectCount.IsNegative));
            Assert.Equal(4, fv.Count());
            Assert.Equal(4, fv.Count(ws));
            Assert.Equal(4, fv.SelectAdd(fScalar).Count(ws));

            // Attempting a reduction under CPU fallback must fail cleanly without touching the active CUDA context
            Assert.Throws<NotSupportedException>(() => v.Sum().Evaluate(ws));
            Assert.Throws<NotSupportedException>(() => fv.Sum(ws));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        // The CUDA context on outerScope must still be healthy and able to run vector reductions on the GPU
        using var gpuWs = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var gpuArray = CreateIntArray(gpuWs, [1, 2, 3, 4]);
        using var gpuSum = gpuArray.AsVectorizedEnumerable().Sum().Evaluate(gpuWs);
        Assert.Equal(new BigInteger(10), gpuSum.Value.ToBigInteger(gpuSum.IsNegative));
    }

    private static GpuBigIntArray CreateIntArray(GpuWorkspace workspace, int[] values)
    {
        var array = new GpuBigIntArray(workspace, values.Length, Words);
        if (values.Length == 0)
        {
            return array;
        }

        var words = new uint[values.Length * Words];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            words[i * Words] = (uint)Math.Abs(values[i]);
            signs[i] = values[i] < 0 ? 1u : 0u;
        }

        workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, words, words.Length, workspace.Stream);
        workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, workspace.Stream);
        return array;
    }

    private static GpuBigFloatArray CreateFloatArray(GpuWorkspace workspace, int[] values)
    {
        var array = new GpuBigFloatArray(workspace, values.Length, Words);
        if (values.Length == 0)
        {
            return array;
        }

        var words = new uint[values.Length * Words];
        var exponents = new uint[values.Length];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            words[i * Words] = (uint)Math.Abs(values[i]);
            exponents[i] = 0u;
            signs[i] = values[i] < 0 ? 1u : 0u;
        }

        workspace.Provider.CopyHostToDevice(array.Significands.DevicePointer, words, words.Length, workspace.Stream);
        workspace.Provider.CopyHostToDevice(array.Exponents.DevicePointer, exponents, exponents.Length, workspace.Stream);
        workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, workspace.Stream);
        return array;
    }
}
