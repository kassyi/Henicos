using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Henicos.Benchmarks;

/// <summary>
/// Times the cooperative mega-kernel NTT operations (<c>InverseSqrt</c>, <c>Reciprocal</c>, <c>ModPow</c>) and
/// standalone 3-prime 35-bit NTT multiplication and squaring across transform lengths <c>2^10</c> through <c>2^24</c>
/// (<c>arithmetic ntt-product</c>, Part of #2132).
/// </summary>
/// <remarks>
/// <para>
/// Each <c>(Kernel, Log2Points = k)</c> pair chooses the largest operand word count <c>n</c> whose baseline transform
/// length is <c>2^k</c> (clamped at <c>2^23</c> words for standalone <c>Multiply</c> and <c>Square</c> at <c>k = 24</c>
/// because <see cref="Ntt35ConvolutionLauncher.TransformLengthFor35"/> caps products at <c>2^24</c> words).
/// </para>
/// <para>
/// <see cref="NttProductKernel.ModPow"/> is measured for <c>k = 10..16</c> with a 2-word exponent and
/// <see cref="GpuWorkspace.PolyAlgorithmThreshold"/> set to <c>32 * n</c> so every case takes the cooperative
/// mega-kernel path.
/// </para>
/// </remarks>
[Config(typeof(NttProductBenchmarkConfig))]
public class GpuBigIntNttProductBenchmark : IDisposable
{
    private sealed class NttProductBenchmarkConfig : ManualConfig
    {
        public NttProductBenchmarkConfig() =>
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(2)
                .WithIterationCount(5)
                .AsDefault());
    }

    internal const int MinLog2Points = 10;
    internal const int MaxLog2Points = 24;
    internal const int MaxModPowLog2Points = 16;
    internal const int InverseSqrtMarginWords = 2;
    internal const int ModPowExponentWords = 2;
    internal const int MaxNtt35OperandWords = 1 << 23;

    private GpuKernelLoader.CudaContextScope? _scope;
    private GpuWorkspace? _workspace;
    private GpuBigUInt? _operandA;
    private GpuBigUInt? _operandB;
    private GpuBigUInt? _exponent;
    private GpuBigUInt? _modulus;
    private GpuBigUInt? _result;
    private int _reciprocalTargetBits;

    /// <summary>One valid <c>(Kernel, Log2Points)</c> benchmark case.</summary>
    /// <param name="Kernel">The NTT-backed operation to time.</param>
    /// <param name="Log2Points">Base-2 logarithm <c>k</c> of the baseline NTT transform length <c>2^k</c>.</param>
    public readonly record struct Case(NttProductKernel Kernel, int Log2Points)
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Kernel} k={Log2Points}";
    }

    /// <summary>Gets or sets the measured kernel and log2 transform length.</summary>
    [ParamsSource(nameof(Cases))]
    public Case Target { get; set; }

    /// <summary>
    /// Gets every valid <c>(Kernel, Log2Points)</c> combination: <c>k = 10..24</c> for
    /// <see cref="NttProductKernel.InverseSqrt"/>, <see cref="NttProductKernel.Reciprocal"/>,
    /// <see cref="NttProductKernel.Multiply"/>, and <see cref="NttProductKernel.Square"/>;
    /// <c>k = 10..16</c> for <see cref="NttProductKernel.ModPow"/>.
    /// </summary>
    public static IEnumerable<Case> Cases()
    {
        for (var k = MinLog2Points; k <= MaxLog2Points; k++)
        {
            yield return new Case(NttProductKernel.InverseSqrt, k);
        }

        for (var k = MinLog2Points; k <= MaxLog2Points; k++)
        {
            yield return new Case(NttProductKernel.Reciprocal, k);
        }

        for (var k = MinLog2Points; k <= MaxModPowLog2Points; k++)
        {
            yield return new Case(NttProductKernel.ModPow, k);
        }

        for (var k = MinLog2Points; k <= MaxLog2Points; k++)
        {
            yield return new Case(NttProductKernel.Multiply, k);
        }

        for (var k = MinLog2Points; k <= MaxLog2Points; k++)
        {
            yield return new Case(NttProductKernel.Square, k);
        }
    }

    /// <summary>
    /// Computes the target bit precision <c>64 * n + 512</c> used by <see cref="NttProductKernel.Reciprocal"/>.
    /// </summary>
    internal static int ReciprocalTargetBits(int operandWords) => 64 * operandWords + 512;

    /// <summary>
    /// Computes the largest operand word count <c>n</c> whose baseline NTT transform length is <c>2^log2Points</c>.
    /// </summary>
    internal static int OperandWordsFor(NttProductKernel kernel, int log2Points)
    {
        var maxK = kernel == NttProductKernel.ModPow ? MaxModPowLog2Points : MaxLog2Points;
        if (log2Points < MinLog2Points || log2Points > maxK)
        {
            throw new ArgumentOutOfRangeException(
                nameof(log2Points),
                log2Points,
                $"Log2Points for {kernel} must be between {MinLog2Points} and {maxK}.");
        }

        var half = 1 << (log2Points - 1);
        return kernel switch
        {
            NttProductKernel.InverseSqrt => (half - 6) / 4,
            NttProductKernel.Reciprocal => (half - 32) / 5,
            NttProductKernel.ModPow => (half - 16) / 5,
            NttProductKernel.Multiply or NttProductKernel.Square =>
                (int)Math.Min(((1L << log2Points) * 35L) / 64L, MaxNtt35OperandWords),
            _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown NTT product kernel.")
        };
    }

    /// <summary>
    /// Queries the live <c>GpuMath</c> launcher for the baseline NTT transform length of <paramref name="kernel"/>
    /// at <paramref name="operandWords"/> words.
    /// </summary>
    internal static int LiveTransformLengthFor(NttProductKernel kernel, int operandWords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(operandWords, 1);
        return kernel switch
        {
            NttProductKernel.InverseSqrt =>
                CooperativeMegaKernelLauncher.TransformLengthFor(
                    CooperativeMegaKernelLauncher.InverseSqrtMegaProductWords(operandWords, InverseSqrtMarginWords)),
            NttProductKernel.Reciprocal =>
                CooperativeMegaKernelLauncher.TransformLengthFor(
                    operandWords + 2 * ((ReciprocalTargetBits(operandWords) + 31) / 32)),
            NttProductKernel.ModPow =>
                CooperativeMegaKernelLauncher.TransformLengthFor(5 * operandWords + 16),
            NttProductKernel.Multiply or NttProductKernel.Square =>
                Ntt35ConvolutionLauncher.TransformLengthFor35(2 * operandWords),
            _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown NTT product kernel.")
        };
    }

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            _scope = new GpuKernelLoader.CudaContextScope();
            var n = OperandWordsFor(Target.Kernel, Target.Log2Points);
            var workspaceWords = Target.Kernel switch
            {
                NttProductKernel.Reciprocal => ReciprocalTargetBits(n) / 32 + 1,
                NttProductKernel.Multiply or NttProductKernel.Square => 2 * n,
                NttProductKernel.InverseSqrt => n + InverseSqrtMarginWords,
                _ => n
            };

            var workspace = new GpuWorkspace(Math.Max(4096, workspaceWords));
            _workspace = workspace;

            var random = new Random(2132);
            switch (Target.Kernel)
            {
                case NttProductKernel.InverseSqrt:
                    _operandA = new GpuBigUInt(workspace, RandomWords(random, n, setTopBit: true));
                    _result = new GpuBigUInt(workspace, n + InverseSqrtMarginWords);
                    break;

                case NttProductKernel.Reciprocal:
                    _reciprocalTargetBits = ReciprocalTargetBits(n);
                    _operandA = new GpuBigUInt(workspace, RandomWords(random, n, setTopBit: true));
                    _result = new GpuBigUInt(workspace, _reciprocalTargetBits / 32 + 1);
                    break;

                case NttProductKernel.ModPow:
                    workspace.PolyAlgorithmThreshold = 32 * n;
                    var modWords = RandomWords(random, n, setTopBit: true);
                    modWords[0] |= 1u;
                    var baseWords = RandomWords(random, n, setTopBit: false);
                    baseWords[^1] &= 0x7FFF_FFFFu;
                    var expWords = RandomWords(random, ModPowExponentWords, setTopBit: true);
                    _operandA = new GpuBigUInt(workspace, baseWords);
                    _exponent = new GpuBigUInt(workspace, expWords);
                    _modulus = new GpuBigUInt(workspace, modWords);
                    _result = new GpuBigUInt(workspace, n);
                    break;

                case NttProductKernel.Multiply:
                    _operandA = new GpuBigUInt(workspace, RandomWords(random, n, setTopBit: true));
                    _operandB = new GpuBigUInt(workspace, RandomWords(random, n, setTopBit: true));
                    _result = new GpuBigUInt(workspace, 2 * n);
                    break;

                case NttProductKernel.Square:
                    _operandA = new GpuBigUInt(workspace, RandomWords(random, n, setTopBit: true));
                    _result = new GpuBigUInt(workspace, 2 * n);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(Target), Target.Kernel, "Unknown NTT product kernel.");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Dispose();
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Runs the selected NTT-backed operation once and synchronizes the CUDA stream.</summary>
    [Benchmark]
    public void Run()
    {
        var workspace = _workspace ?? throw new InvalidOperationException("The benchmark is not set up.");
        switch (Target.Kernel)
        {
            case NttProductKernel.InverseSqrt:
                GpuBigIntDivRem.InverseSqrt(Buffer(_operandA), Buffer(_result), marginWords: InverseSqrtMarginWords);
                break;

            case NttProductKernel.Reciprocal:
                GpuBigIntDivRem.Reciprocal(Buffer(_operandA), Buffer(_result), targetBits: _reciprocalTargetBits);
                break;

            case NttProductKernel.ModPow:
                GpuBigIntDivRem.ModPow(Buffer(_operandA), Buffer(_exponent), Buffer(_modulus), Buffer(_result));
                break;

            case NttProductKernel.Multiply:
                GpuBigIntMath.Mul(Buffer(_operandA), Buffer(_operandB), Buffer(_result));
                break;

            case NttProductKernel.Square:
                GpuBigIntMath.Mul(Buffer(_operandA), Buffer(_operandA), Buffer(_result));
                break;

            default:
                throw new InvalidOperationException($"Unknown NTT product kernel: {Target.Kernel}.");
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        GpuKernelLoader.ThrowIfDeviceError();
    }

    public void Dispose()
    {
        _result?.Dispose();
        _modulus?.Dispose();
        _exponent?.Dispose();
        _operandB?.Dispose();
        _operandA?.Dispose();
        _workspace?.Dispose();
        _scope?.Dispose();
        _result = null;
        _modulus = null;
        _exponent = null;
        _operandB = null;
        _operandA = null;
        _workspace = null;
        _scope = null;
        GC.SuppressFinalize(this);
    }

    private static GpuBigUInt Buffer(GpuBigUInt? buffer) =>
        buffer ?? throw new InvalidOperationException("The benchmark is not set up.");

    private static uint[] RandomWords(Random random, int count, bool setTopBit)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)random.NextInt64(0, 1L << 32);
        }

        if (setTopBit)
        {
            words[^1] |= 0x8000_0000u;
        }

        return words;
    }
}
