namespace Henicos.PiBenchmark.ComputeBenchmarks;

public class BlackScholesBenchmark : IComputeBenchmark
{
    private const int N = 1_000_000;
    
    private float[] _s = null!;
    private float[] _x = null!;
    private float[] _t = null!;
    private float _r;
    private float _v;
    
    private float[] _callResultCpu = null!;
    private float[] _putResultCpu = null!;
    
    private float[] _callResultGsharp = null!;
    private float[] _putResultGsharp = null!;

    public string Name => "Black-Scholes (Float)";

    public void Initialize()
    {
        _s = new float[N];
        _x = new float[N];
        _t = new float[N];
        _callResultCpu = new float[N];
        _putResultCpu = new float[N];
        _callResultGsharp = new float[N];
        _putResultGsharp = new float[N];

        _r = 0.02f;
        _v = 0.30f;

        var rand = new Random(42);
        for (int i = 0; i < N; i++)
        {
            _s[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Spot price 10..110
            _x[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Strike price 10..110
            _t[i] = (float)(rand.NextDouble() * 5.0 + 0.1);    // Time to maturity 0.1..5.1
        }
    }

    public void RunReferenceCpu()
    {
        for (int i = 0; i < N; i++)
        {
            BlackScholesCore(_s[i], _x[i], _t[i], _r, _v, out _callResultCpu[i], out _putResultCpu[i]);
        }
    }

    public void RunGSharpCpu()
    {
        // G# Kernel Execution via Source Generated CPU Wrapper
        BlackScholesKernelsWrapper.Launch_BlackScholesKernel(
            _s, _x, _t, _r, _v, _callResultGsharp, _putResultGsharp, N,
            gridDimX: 1, blockDimX: (uint)N);
    }

    public void Verify()
    {
        for (int i = 0; i < N; i++)
        {
            if (Math.Abs(_callResultCpu[i] - _callResultGsharp[i]) > 1e-4f)
            {
                throw new Exception($"Call price mismatch at index {i}: CPU={_callResultCpu[i]}, G#={_callResultGsharp[i]}");
            }
            if (Math.Abs(_putResultCpu[i] - _putResultGsharp[i]) > 1e-4f)
            {
                throw new Exception($"Put price mismatch at index {i}: CPU={_putResultCpu[i]}, G#={_putResultGsharp[i]}");
            }
        }
    }

    // Baseline C# Implementation
    private static void BlackScholesCore(float s, float x, float t, float r, float v, out float callResult, out float putResult)
    {
        float d1 = (MathF.Log(s / x) + (r + v * v / 2.0f) * t) / (v * MathF.Sqrt(t));
        float d2 = d1 - v * MathF.Sqrt(t);

        float cndD1 = CND(d1);
        float cndD2 = CND(d2);

        float expRT = MathF.Exp(-r * t);
        
        callResult = s * cndD1 - x * expRT * cndD2;
        putResult = x * expRT * (1.0f - cndD2) - s * (1.0f - cndD1);
    }

    private static float CND(float d)
    {
        const float A1 = 0.31938153f;
        const float A2 = -0.356563782f;
        const float A3 = 1.781477937f;
        const float A4 = -1.821255978f;
        const float A5 = 1.330274429f;
        const float RSQRT2PI = 0.39894228040143267793994605993438f;

        float K = 1.0f / (1.0f + 0.2316419f * MathF.Abs(d));
        float cnd = RSQRT2PI * MathF.Exp(-0.5f * d * d) * 
                    (K * (A1 + K * (A2 + K * (A3 + K * (A4 + K * A5)))));

        if (d > 0)
        {
            cnd = 1.0f - cnd;
        }

        return cnd;
    }
}
