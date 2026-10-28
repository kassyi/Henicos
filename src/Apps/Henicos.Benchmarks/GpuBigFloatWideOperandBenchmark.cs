using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

namespace Henicos.Benchmarks;

/// <summary>
/// The big-float <c>Divide</c> and <c>Rsqrt</c> of <see cref="GSharpKernelFloatProvider"/> with a divisor or radicand
/// much wider than the result, with the cut to the top <c>n + 2</c> words forced on and forced off (#661 §4.12, #767).
/// </summary>
/// <remarks>
/// The ratio of the two at each width shows where the cut pays off; <see cref="WideOperandOptions"/> takes its defaults
/// from this benchmark (<c>arithmetic wide</c>).
/// </remarks>
[Config(typeof(WideOperandBenchmarkConfig))]
public class GpuBigFloatWideOperandBenchmark : IDisposable
{
    private sealed class WideOperandBenchmarkConfig : ManualConfig
    {
        public WideOperandBenchmarkConfig() =>
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(3)
                .WithIterationCount(10)
                .AsDefault());
    }

    // The result widths measured with operands of 4n and 16n words, and the narrow operands measured for small results.
    private static readonly int[] s_resultWords = [8, 16, 24, 30, 31, 32, 40, 44, 45, 48, 64];

    private static readonly (int ResultWords, int OperandWords)[] s_narrowOperands =
        [(1, 7), (1, 32), (4, 10), (4, 24), (8, 14), (8, 24), (16, 22), (16, 34)];

    private GpuKernelLoader.CudaContextScope? _scope;
    // Created in [GlobalSetup] before any benchmark runs; a non-null field so that the ownership analyzer (GSH0022)
    // sees Dispose() call _workspace.Dispose().
    private GpuWorkspace _workspace = null!;
    private GpuBigUInt? _dividend;
    private GpuBigUInt? _wide;
    private GpuBigUInt? _result;

    /// <summary>A result of <c>n</c> words and a divisor or radicand of <c>w</c> words.</summary>
    /// <param name="ResultWords">The number <c>n</c> of result words.</param>
    /// <param name="OperandWords">The number <c>w</c> of words of the divisor and of the radicand.</param>
    public readonly record struct Shape(int ResultWords, int OperandWords)
    {
        /// <inheritdoc/>
        public override string ToString() => $"n={ResultWords} w={OperandWords}";
    }

    /// <summary>Gets or sets the result and operand widths.</summary>
    [ParamsSource(nameof(Shapes))]
    public Shape Widths { get; set; }

    /// <summary>Gets or sets whether the cut is forced on (<see langword="true"/>) or off.</summary>
    [Params(true, false)]
    public bool Cut { get; set; }

    /// <summary>
    /// Gets the measured widths: operands of <c>4n</c> and <c>16n</c> words around the result widths where the cut
    /// stops paying off, and operands of up to 32 words, near the threshold <c>n + 2 + g</c>, for small results.
    /// </summary>
    public static IEnumerable<Shape> Shapes()
    {
        foreach (var n in s_resultWords)
        {
            yield return new Shape(n, 4 * n);
            yield return new Shape(n, 16 * n);
        }

        foreach (var (n, w) in s_narrowOperands)
        {
            yield return new Shape(n, w);
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(4096);
        var options = _workspace.WideOperands;
        options.ThresholdWords = Cut ? 0 : int.MaxValue;
        options.DivideMaxResultWords = int.MaxValue;
        options.DivideMinDivisorWords = 0;
        options.RsqrtMaxResultWords = int.MaxValue;

        // Seeded random values with their top bit set, so the operands have their full width.
        var random = new Random(767);
        _dividend = new GpuBigUInt(_workspace, RandomWords(random, Widths.ResultWords));
        _wide = new GpuBigUInt(_workspace, RandomWords(random, Widths.OperandWords));
        _result = new GpuBigUInt(_workspace, Widths.ResultWords);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>A dividend of <c>n</c> words over the wide divisor, rounded to <c>n</c> words.</summary>
    [Benchmark]
    public void Divide()
    {
        var workspace = _workspace;
        var context = GpuWorkspaceArithmetic.CreateContext(workspace);
        var left = new GpuFloatOperand(Buffer(_dividend).Handle, 0);
        var right = new GpuFloatOperand(Buffer(_wide).Handle, 0);
        GSharpKernelFloatProvider.Instance.Divide(ref context, in left, in right, Buffer(_result).Handle);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
    }

    /// <summary>The inverse square root of the wide radicand, rounded to <c>n</c> words.</summary>
    [Benchmark]
    public void Rsqrt()
    {
        var workspace = _workspace;
        var context = GpuWorkspaceArithmetic.CreateContext(workspace);
        var value = new GpuFloatOperand(Buffer(_wide).Handle, 0);
        GSharpKernelFloatProvider.Instance.Rsqrt(ref context, in value, Buffer(_result).Handle);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
    }

    public void Dispose()
    {
        _result?.Dispose();
        _wide?.Dispose();
        _dividend?.Dispose();
        _workspace.Dispose();
        _scope?.Dispose();
        _result = null;
        _wide = null;
        _dividend = null;
        _scope = null;
        GC.SuppressFinalize(this);
    }

    private static GpuBigUInt Buffer(GpuBigUInt? buffer) =>
        buffer ?? throw new InvalidOperationException("The benchmark is not set up.");

    private static uint[] RandomWords(Random random, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)random.NextInt64(0, 1L << 32);
        }

        words[^1] |= 0x8000_0000u;
        return words;
    }
}
