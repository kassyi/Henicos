namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A struct whose <c>fixed</c> buffers are sized by constants, declared in another file than the kernels that use it
/// (<see cref="StructDifferentialKernels.ConstantSizedFixedBufferKernel"/>).
/// </summary>
public unsafe struct DiffConstantSizedPacket
{
    public const int Size = 3;
    public const int WideSize = (Size * 2) + 1;

    public int Head;
    public fixed int Items[Size];
    public fixed short Wide[WideSize];
    public int Tail;
}
