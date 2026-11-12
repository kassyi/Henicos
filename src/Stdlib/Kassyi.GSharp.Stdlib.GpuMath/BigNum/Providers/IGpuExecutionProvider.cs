namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines the aggregated contract for executing GPU-accelerated BigInteger arithmetic algorithms, managing device memory, and capturing CUDA graphs.
/// </summary>
public interface IGpuExecutionProvider :
    IGpuMemoryProvider,
    IGpuBasicMathProvider,
    IGpuAdvancedMathProvider,
    IGpuGraphProvider;
