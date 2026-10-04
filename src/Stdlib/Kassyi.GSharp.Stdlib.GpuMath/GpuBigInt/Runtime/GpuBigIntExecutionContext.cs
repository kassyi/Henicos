namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Runtime;

using System;

public readonly ref struct GpuBigIntExecutionContext(
    GpuWorkspace gpuWorkspace,
    IGpuMemoryManager memoryManager,
    IGpuBigIntArithmetic arithmetic,
    IntPtr stream)
{
    public GpuWorkspace GpuWorkspace { get; } = gpuWorkspace;
    public IGpuMemoryManager MemoryManager { get; } = memoryManager;
    public IGpuBigIntArithmetic Arithmetic { get; } = arithmetic;
    public IntPtr Stream { get; } = stream;

    public GpuBigIntExecutionContext(GpuWorkspace gpuWorkspace, IGpuMemoryManager memoryManager, IntPtr stream)
        : this(gpuWorkspace, memoryManager, default(LegacyProviderArithmeticAdapter), stream)
    {
    }
}
