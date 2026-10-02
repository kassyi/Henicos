namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Runtime;

public readonly record struct GpuBigIntMemoryHandle
{
    internal GpuBigIntDeviceBuffer Buffer { get; }
    public int WordOffset { get; }
    public int NumWords { get; }

    internal GpuBigIntMemoryHandle(GpuBigIntDeviceBuffer buffer, int wordOffset, int numWords)
    {
        Buffer = buffer;
        WordOffset = wordOffset;
        NumWords = numWords;
    }

    public bool IsDefault => Buffer == null;
}
