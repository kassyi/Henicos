namespace Kassyi.GSharp.Stdlib.Math.Contexts;

/// <summary>
/// Execution arguments for NTT-related kernels.
/// </summary>
public struct NumberTheoreticTransformKernelArgs
{
    public uint[] Buffer;
    public uint[] TemporaryBuffer;

    [GpuConst]
    public uint[] Twiddle;

    public int M;
    public int N1;
    public int N2;
    public uint P;
    public ulong Pm;
    public bool IsInverse;
    public bool ApplyTwiddleBefore;
    public bool ApplyTwiddleAfter;
    public int MaxBlocks;
    public int CurrentL;
}

