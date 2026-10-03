namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class BlackScholesKernels
{
    public struct BlackScholesContext
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


    [GpuKernel]
    public static void BlackScholesKernel(BlackScholesContext ctx)
    {
        Gpu.GridStrideLoop(ctx.N, tid =>
        {
            CalculateOptionPrice(ctx.S[tid], ctx.X[tid], ctx.T[tid], ctx.R, ctx.V, out var c, out var p);
            ctx.CallResult[tid] = c;
            ctx.PutResult[tid] = p;
        });
    }

    [GpuDeviceFunction]
    public static void CalculateOptionPrice(float s, float x, float t, float r, float v, out float callResult,
        out float putResult)
    {
        var d1 = (FastMath.Log(s / x) + (r + v * v / 2.0f) * t) / (v * FastMath.Sqrt(t));
        var d2 = d1 - v * FastMath.Sqrt(t);

        var cndD1 = Cnd(d1);
        var cndD2 = Cnd(d2);

        var expRt = FastMath.Exp(-r * t);

        callResult = s * cndD1 - x * expRt * cndD2;
        putResult = x * expRt * (1.0f - cndD2) - s * (1.0f - cndD1);
    }

    [GpuDeviceFunction]
    public static float Cnd(float d)
    {
        var k = 1.0f / (1.0f + 0.2316419f * FastMath.Abs(d));
        var cnd = 0.3989422804f * FastMath.Exp(-0.5f * d * d) *
                  (k * (0.31938153f +
                        k * (-0.356563782f + k * (1.781477937f + k * (-1.821255978f + k * 1.330274429f)))));

        if (d > 0)
        {
            cnd = 1.0f - cnd;
        }

        return cnd;
    }
}
