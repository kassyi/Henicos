using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;

namespace Henicos.Benchmarks;

/// <summary>
/// Benchmarks <c>GpuBigIntDivRem.ModPow</c> above the 1024-bit poly-algorithm threshold where
/// <c>ModPowZeroSyncController</c> runs (<c>arithmetic modpow</c>, Part of #2346).
/// </summary>
[Config(typeof(ModPowBenchmarkConfig))]
public class GpuBigIntModPowBenchmark : IDisposable
{
    private sealed class ModPowBenchmarkConfig : ManualConfig
    {
        public ModPowBenchmarkConfig() =>
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(3)
                .WithIterationCount(10)
                .AsDefault());
    }

    private static readonly int[] s_modulusBits = [2048, 4096, 8192, 16384, 32768];
    private static readonly int[] s_exponentBits = [64, 1024];

    private GpuKernelLoader.CudaContextScope? _scope;
    private GpuWorkspace? _workspace;
    private GpuBigUInt? _base;
    private GpuBigUInt? _exp;
    private GpuBigUInt? _mod;
    private GpuBigUInt? _result;

    /// <summary>Gets or sets the modulus width in bits (greater than 1024).</summary>
    [ParamsSource(nameof(ModulusBitsValues))]
    public int ModulusBits { get; set; }

    /// <summary>Gets or sets the exponent width in bits.</summary>
    [ParamsSource(nameof(ExponentBitsValues))]
    public int ExponentBits { get; set; }

    /// <summary>Gets the measured modulus bit widths for BenchmarkDotNet parameter expansion.</summary>
    public static IEnumerable<int> ModulusBitsValues() => s_modulusBits;

    /// <summary>Gets the measured exponent bit widths for BenchmarkDotNet parameter expansion.</summary>
    public static IEnumerable<int> ExponentBitsValues() => s_exponentBits;

    /// <summary>
    /// Creates deterministic seeded operands with an odd full-width modulus, <c>base &lt; mod</c>, and an exponent
    /// whose highest bit is at <paramref name="exponentBits"/> - 1.
    /// </summary>
    internal static (uint[] Base, uint[] Exponent, uint[] Modulus) CreateInputs(int modulusBits, int exponentBits)
    {
        if (modulusBits <= 0 || modulusBits % 32 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modulusBits), modulusBits, "ModulusBits must be a positive multiple of 32.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(exponentBits, 1);

        var modWords = modulusBits / 32;
        var expWords = (exponentBits + 31) / 32;
        var random = new Random(2346);

        var modData = RandomWords(random, modWords);
        modData[0] |= 1u;
        modData[^1] |= 0x8000_0000u;

        var baseData = RandomWords(random, modWords);
        baseData[^1] = (baseData[^1] & 0x7FFF_FFFFu) | 0x4000_0000u;

        var expData = RandomWords(random, expWords);
        var topBitInLastWord = (exponentBits - 1) % 32;
        var topWordMask = topBitInLastWord == 31 ? uint.MaxValue : (1u << (topBitInLastWord + 1)) - 1u;
        expData[^1] = (expData[^1] & topWordMask) | (1u << topBitInLastWord);

        return (baseData, expData, modData);
    }

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            var (baseData, expData, modData) = CreateInputs(ModulusBits, ExponentBits);
            var modWords = modData.Length;

            _scope = new GpuKernelLoader.CudaContextScope();
            var workspace = new GpuWorkspace(Math.Max(4096, 6 * modWords));
            _workspace = workspace;

            _base = new GpuBigUInt(workspace, baseData);
            _exp = new GpuBigUInt(workspace, expData);
            _mod = new GpuBigUInt(workspace, modData);
            _result = new GpuBigUInt(workspace, modWords);

            // Warm up module loading and pooled device buffers before BenchmarkDotNet measurement.
            ModPow();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Dispose();
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Computes modular exponentiation of the seeded operands into the result buffer.</summary>
    [Benchmark]
    public void ModPow()
    {
        var workspace = _workspace ?? throw new InvalidOperationException("The benchmark is not set up.");
        GpuBigIntDivRem.ModPow(Buffer(_base), Buffer(_exp), Buffer(_mod), Buffer(_result));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        GpuKernelLoader.ThrowIfDeviceError();
    }

    public void Dispose()
    {
        _result?.Dispose();
        _mod?.Dispose();
        _exp?.Dispose();
        _base?.Dispose();
        _workspace?.Dispose();
        _scope?.Dispose();
        _result = null;
        _mod = null;
        _exp = null;
        _base = null;
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

        return words;
    }
}
