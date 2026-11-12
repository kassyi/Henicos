using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

internal static class BenchmarkKernelResourceHelper
{
    private static readonly Dictionary<Type, Func<IComputeBenchmark>> s_typeFactories = new()
    {
        { typeof(SgemmBenchmark), () => new SgemmBenchmark() },
        { typeof(Sha256Benchmark), () => new Sha256Benchmark() },
        { typeof(BlackScholesBenchmark), () => new BlackScholesBenchmark() },
        { typeof(ReductionBenchmark), () => new ReductionBenchmark() }
    };

    private static readonly Dictionary<Type, (string Regs, string Spill)> s_cache = new();
    private static readonly Lock s_lock = new();

    public static (string Regs, string Spill) GetOrInspectResources(Type targetType)
    {
        lock (s_lock)
        {
            if (s_cache.TryGetValue(targetType, out var cached))
            {
                return cached;
            }

            if (!s_typeFactories.TryGetValue(targetType, out var factory))
            {
                return ("-", "-");
            }

            try
            {
                var benchmark = factory();
                benchmark.Initialize();
                var gsharpAttrs = KernelResourceInspector.TryGetAttributes(benchmark.GetGSharpFunction());
                var nativeAttrs = KernelResourceInspector.TryGetAttributes(benchmark.GetNativeFunction());

                var gsharpRegs = gsharpAttrs?.NumRegs.ToString() ?? "-";
                var nativeRegs = nativeAttrs?.NumRegs.ToString() ?? "-";
                var regs = $"{gsharpRegs} / {nativeRegs}";

                var gsharpSpill = gsharpAttrs?.LocalSizeBytes.ToString() ?? "-";
                var nativeSpill = nativeAttrs?.LocalSizeBytes.ToString() ?? "-";
                var spill = $"{gsharpSpill} / {nativeSpill}";

                var result = (regs, spill);
                s_cache[targetType] = result;
                return result;
            }
            catch
            {
                // Ignore initialization failures in column inspection
                s_cache[targetType] = ("-", "-");
                return ("-", "-");
            }
        }
    }
}
