namespace Henicos.Benchmarks.ComputeBenchmarks;

public class BlackScholesBenchmark : IComputeBenchmark
{
    private const int N = 1_000_000;

    private float[] _s = [];
    private float[] _x = [];
    private float[] _t = [];
    private float _r;
    private float _v;

    private float[] _callResultCpu = [];
    private float[] _putResultCpu = [];

    private float[] _callResultGsharp = [];
    private float[] _putResultGsharp = [];

    private float[] _callResultNative = [];
    private float[] _putResultNative = [];

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
        _callResultNative = new float[N];
        _putResultNative = new float[N];

        _r = 0.02f;
        _v = 0.30f;

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _s[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Spot price 10..110
            _x[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Strike price 10..110
            _t[i] = (float)(rand.NextDouble() * 5.0 + 0.1);    // Time to maturity 0.1..5.1
        }
    }

    public void RunReferenceCpu()
    {
        for (var i = 0; i < N; i++)
        {
            BlackScholesKernels.CalculateOptionPrice(_s[i], _x[i], _t[i], _r, _v, out _callResultCpu[i], out _putResultCpu[i]);
        }
    }

    public void RunGSharp()
    {
        uint blockSize = 256;
        var numBlocks = (N + blockSize - 1) / blockSize;
        BlackScholesKernels.LaunchBlackScholesKernel(
            new BlackScholesKernels.BlackScholesContext
            {
                S = _s,
                X = _x,
                T = _t,
                R = _r,
                V = _v,
                CallResult = _callResultGsharp,
                PutResult = _putResultGsharp,
                N = N
            },
            gridDimX: numBlocks, blockDimX: blockSize);
    }

    public void RunNativeCuda()
    {
        uint blockSize = 256;
        var numBlocks = (N + blockSize - 1) / blockSize;
        NativeKernelInvoker.LaunchBlackScholes(
            _s, _x, _t, _r, _v, _callResultNative, _putResultNative, N,
            gridDimX: numBlocks, blockDimX: blockSize);
    }

    public void Verify()
    {
        for (var i = 0; i < N; i++)
        {
            if (Math.Abs(_callResultCpu[i] - _callResultGsharp[i]) > 1e-4f)
            {
                throw new Exception($"Call price mismatch at index {i}: CPU={_callResultCpu[i]}, G#={_callResultGsharp[i]}");
            }
            if (Math.Abs(_putResultCpu[i] - _putResultGsharp[i]) > 1e-4f)
            {
                throw new Exception($"Put price mismatch at index {i}: CPU={_putResultCpu[i]}, G#={_putResultGsharp[i]}");
            }
            if (Math.Abs(_callResultCpu[i] - _callResultNative[i]) > 1e-4f)
            {
                throw new Exception($"Call price mismatch at index {i}: CPU={_callResultCpu[i]}, Native={_callResultNative[i]}");
            }
            if (Math.Abs(_putResultCpu[i] - _putResultNative[i]) > 1e-4f)
            {
                throw new Exception($"Put price mismatch at index {i}: CPU={_putResultCpu[i]}, Native={_putResultNative[i]}");
            }
        }
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionBlackScholes;
    public string NativeKernelName => "BlackScholesKernel_Native";
    public string GSharpKernelName => "BlackScholesKernels_BlackScholesKernel";
}
