namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class BlackScholesKernels
{
    [GpuKernel]
    public static void BlackScholesKernel(float[] s, float[] x, float[] t, float r, float v, float[] callResult,
        float[] putResult, int n)
    {
        int stride = BlockDimension.X * GridDimension.X;
        for (int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X; tid < n; tid += stride)
        {
            float s_val = s[tid];
            float x_val = x[tid];
            float t_val = t[tid];

            float d1 = (MathF.Log(s_val / x_val) + (r + v * v / 2.0f) * t_val) / (v * MathF.Sqrt(t_val));
            float d2 = d1 - v * MathF.Sqrt(t_val);

            float K1 = 1.0f / (1.0f + 0.2316419f * MathF.Abs(d1));
            float cndD1 = 0.3989422804f * MathF.Exp(-0.5f * d1 * d1) *
                        (K1 * (0.31938153f +
                              K1 * (-0.356563782f + K1 * (1.781477937f + K1 * (-1.821255978f + K1 * 1.330274429f)))));
            if (d1 > 0)
            {
                cndD1 = 1.0f - cndD1;
            }

            float K2 = 1.0f / (1.0f + 0.2316419f * MathF.Abs(d2));
            float cndD2 = 0.3989422804f * MathF.Exp(-0.5f * d2 * d2) *
                        (K2 * (0.31938153f +
                              K2 * (-0.356563782f + K2 * (1.781477937f + K2 * (-1.821255978f + K2 * 1.330274429f)))));
            if (d2 > 0)
            {
                cndD2 = 1.0f - cndD2;
            }

            float expRT = MathF.Exp(-r * t_val);

            callResult[tid] = s_val * cndD1 - x_val * expRT * cndD2;
            putResult[tid] = x_val * expRT * (1.0f - cndD2) - s_val * (1.0f - cndD1);
        }
    }
}
