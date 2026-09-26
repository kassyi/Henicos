namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public readonly record struct BigIntMemoryHandle
{
    internal BigIntDeviceBuffer Buffer { get; }
    public int WordOffset { get; }
    public int NumWords { get; }

    internal BigIntMemoryHandle(BigIntDeviceBuffer buffer, int wordOffset, int numWords)
    {
        Buffer = buffer;
        WordOffset = wordOffset;
        NumWords = numWords;
    }
    
    public bool IsDefault => Buffer == null;
}
