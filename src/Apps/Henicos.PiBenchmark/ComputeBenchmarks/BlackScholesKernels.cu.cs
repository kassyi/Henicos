namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class BlackScholesKernels
{
    [GpuKernel]
    public static void BlackScholesKernel(float[] s, float[] x, float[] t, float r, float v, float[] callResult,
        float[] putResult, int n)
    {
        Gpu.GridStrideLoop(n, tid =>
        {
            float sVal = s[tid];
            float xVal = x[tid];
            float tVal = t[tid];

            float d1 = (MathF.Log(sVal / xVal) + (r + v * v / 2.0f) * tVal) / (v * MathF.Sqrt(tVal));
            float d2 = d1 - v * MathF.Sqrt(tVal);

            float cndD1 = CndGsharp(d1);
            float cndD2 = CndGsharp(d2);

            float expRt = MathF.Exp(-r * tVal);

            callResult[tid] = sVal * cndD1 - xVal * expRt * cndD2;
            putResult[tid] = xVal * expRt * (1.0f - cndD2) - sVal * (1.0f - cndD1);
        });
    }

    [GpuDeviceFunction]
    private static float CndGsharp(float d)
    {
        float k = 1.0f / (1.0f + 0.2316419f * MathF.Abs(d));
        float cnd = 0.3989422804f * MathF.Exp(-0.5f * d * d) *
                    (k * (0.31938153f +
                          k * (-0.356563782f + k * (1.781477937f + k * (-1.821255978f + k * 1.330274429f)))));

        if (d > 0)
        {
            cnd = 1.0f - cnd;
        }

        return cnd;
    }
}
