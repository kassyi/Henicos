using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Henicos.Benchmarks;

/// <summary>
/// Compares the two inverse-square-root paths behind <c>GpuBigIntDivRem.Sqrt</c> across the NTT
/// transform-length doubling boundaries: cooperative mega-kernel vs host-driven precision doubling
/// (<c>arithmetic sqrt-path</c>, Part of #2225).
/// </summary>
/// <remarks>
/// <para>
/// For a <c>2r</c>-word radicand with MSB set, <c>GpuBigIntDivRem.Sqrt</c> runs <c>SqrtFloor</c> with
/// <c>n = r + 1</c> top words and <c>m = ceil(r / 2)</c> margin words (<c>GpuBigIntDivRem.SqrtFloorMarginWords(r)</c>).
/// In the mega-kernel path, the Newton product width is <c>maxOutWords = 4(r + 1) + 3 * ceil(r / 2)</c> words, and
/// the 2-prime NTT transform length is <c>M = 2^ceil(log2(2 * maxOutWords))</c>.
/// </para>
/// <para>
/// The 20 measured <see cref="RootWords"/> values are the pairs <c>(r, r + 1)</c> bracketing the 10 doubling
/// boundaries from <c>M = 512</c> (<c>r = 45</c>) to <c>M = 524288</c> (<c>r = 23831</c>):
/// <c>(45, 46)</c>, <c>(92, 93)</c>, <c>(185, 186)</c>, <c>(371, 372)</c>, <c>(744, 745)</c>,
/// <c>(1488, 1489)</c>, <c>(2978, 2979)</c>, <c>(5956, 5957)</c>, <c>(11914, 11915)</c>, <c>(23830, 23831)</c>.
/// </para>
/// </remarks>
[Config(typeof(SqrtPathBenchmarkConfig))]
public class GpuBigIntSqrtPathBenchmark : IDisposable
{
    private sealed class SqrtPathBenchmarkConfig : ManualConfig
    {
        public SqrtPathBenchmarkConfig() =>
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(3)
                .WithIterationCount(10)
                .AsDefault());
    }

    private static readonly int[] s_boundaryRootWords =
    [
        45, 46,
        92, 93,
        185, 186,
        371, 372,
        744, 745,
        1488, 1489,
        2978, 2979,
        5956, 5957,
        11914, 11915,
        23830, 23831,
    ];

    private GpuKernelLoader.CudaContextScope? _scope;
    private GpuWorkspace? _workspace;
    private GpuBigUInt? _operand;
    private GpuBigUInt? _result;

    /// <summary>Gets the root word counts on both sides of the 10 NTT doubling boundaries.</summary>
    internal static IReadOnlyList<int> BoundaryRootWords => s_boundaryRootWords;

    /// <summary>Gets or sets the root word count <c>r</c>; the radicand has <c>2r</c> words with MSB set.</summary>
    [ParamsSource(nameof(RootWordsValues))]
    public int RootWords { get; set; }

    /// <summary>
    /// Gets or sets whether the inverse square root is forced to the cooperative mega-kernel
    /// (<see langword="true"/>: threshold <see cref="int.MaxValue"/>) or precision doubling
    /// (<see langword="false"/>: threshold <c>0</c>).
    /// </summary>
    [Params(true, false)]
    public bool MegaKernel { get; set; }

    /// <summary>Gets the measured root word counts for BenchmarkDotNet parameter expansion.</summary>
    public static IEnumerable<int> RootWordsValues() => s_boundaryRootWords;

    /// <summary>
    /// Maps <see cref="MegaKernel"/> to <see cref="GpuWorkspace.SqrtPrecisionDoublingThresholdWords"/>.
    /// </summary>
    internal static int ThresholdWordsFor(bool megaKernel) => megaKernel ? int.MaxValue : 0;

    /// <summary>
    /// Computes the mega-kernel 2-prime NTT transform length <c>M</c> for a root of <paramref name="rootWords"/>
    /// words using <c>GpuMath</c>'s margin and transform-length helpers.
    /// </summary>
    internal static int MegaKernelTransformLength(int rootWords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rootWords, 1);
        var marginWords = GpuBigIntDivRem.SqrtFloorMarginWords(rootWords);
        var maxOutWords = 4 * (rootWords + 1) + 3 * marginWords;
        return CooperativeMegaKernelLauncher.TransformLengthFor(maxOutWords);
    }

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();
        var workspace = new GpuWorkspace(Math.Max(4096, 2 * RootWords));
        workspace.SqrtPrecisionDoublingThresholdWords = ThresholdWordsFor(MegaKernel);
        _workspace = workspace;

        // Seeded random operand of 2 * RootWords words with its top bit set, so the root fills RootWords words.
        var random = new Random(2225);
        _operand = new GpuBigUInt(workspace, RandomWords(random, 2 * RootWords));
        _result = new GpuBigUInt(workspace, RootWords);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Computes the integer square root of the <c>2r</c>-word operand into an <c>r</c>-word buffer.</summary>
    [Benchmark]
    public void Sqrt()
    {
        var workspace = _workspace ?? throw new InvalidOperationException("The benchmark is not set up.");
        GpuBigIntDivRem.Sqrt(Buffer(_operand), Buffer(_result));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));

        global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.ThrowIfDeviceError();
    }

    public void Dispose()
    {
        _result?.Dispose();
        _operand?.Dispose();
        _workspace?.Dispose();
        _scope?.Dispose();
        _result = null;
        _operand = null;
        _workspace = null;
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
