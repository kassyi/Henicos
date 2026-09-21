using System.Runtime.InteropServices;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class BlackScholesKernels
{
    [GpuKernel]
    public static void BlackScholesKernel([In] float[] s, [In] float[] x, [In] float[] t, float r, float v, [Out] float[] callResult,
        [Out] float[] putResult, int n)
    {
        Gpu.GridStrideLoop(n, tid =>
        {
            float s_val = s[tid];
            float x_val = x[tid];
            float t_val = t[tid];

            float d1 = (FastMath.Log(s_val / x_val) + (r + v * v / 2.0f) * t_val) / (v * FastMath.Sqrt(t_val));
            float d2 = d1 - v * FastMath.Sqrt(t_val);

            float cndD1 = CndGsharp(d1);
            float cndD2 = CndGsharp(d2);

            float expRT = FastMath.Exp(-r * t_val);

            callResult[tid] = s_val * cndD1 - x_val * expRT * cndD2;
            putResult[tid] = x_val * expRT * (1.0f - cndD2) - s_val * (1.0f - cndD1);
        });
    }

    [GpuDeviceFunction]
    private static float CndGsharp(float d)
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
