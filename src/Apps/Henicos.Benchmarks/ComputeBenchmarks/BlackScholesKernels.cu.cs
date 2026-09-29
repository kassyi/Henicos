using System.Runtime.InteropServices;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class BlackScholesKernels
{
    public static class AotContexts
    {
        public struct BlackScholes
        {
            [GpuReadOnly]
            public float[] S;

            [GpuReadOnly]
            public float[] X;

            [GpuReadOnly]
            public float[] T;

            public float R;
            public float V;
            public float[] CallResult;
            public float[] PutResult;
            public int N;
        }
    }


    [GpuKernel]
    public static void BlackScholesKernel(AotContexts.BlackScholes ctx)
    {
        Gpu.GridStrideLoop(ctx.N, tid =>
        {
            CalculateOptionPrice(ctx.S[tid], ctx.X[tid], ctx.T[tid], ctx.R, ctx.V, out float c, out float p);
            ctx.CallResult[tid] = c;
            ctx.PutResult[tid] = p;
        });
    }

    [GpuDeviceFunction]
    public static void CalculateOptionPrice(float s, float x, float t, float r, float v, out float callResult,
        out float putResult)
    {
        float d1 = (FastMath.Log(s / x) + (r + v * v / 2.0f) * t) / (v * FastMath.Sqrt(t));
        float d2 = d1 - v * FastMath.Sqrt(t);

        float cndD1 = Cnd(d1);
        float cndD2 = Cnd(d2);

        float expRT = FastMath.Exp(-r * t);

        callResult = s * cndD1 - x * expRT * cndD2;
        putResult = x * expRT * (1.0f - cndD2) - s * (1.0f - cndD1);
    }

    [GpuDeviceFunction]
    public static float Cnd(float d)
    {
        float K = 1.0f / (1.0f + 0.2316419f * FastMath.Abs(d));
        float cnd = 0.3989422804f * FastMath.Exp(-0.5f * d * d) *
                    (K * (0.31938153f +
                          K * (-0.356563782f + K * (1.781477937f + K * (-1.821255978f + K * 1.330274429f)))));

        if (d > 0)
        {
            cnd = 1.0f - cnd;
        }

        return cnd;
    }
}
