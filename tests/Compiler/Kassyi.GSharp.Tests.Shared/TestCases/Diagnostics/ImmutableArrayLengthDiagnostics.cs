using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace TestNs;

public static partial class ImmutableLengthKernels
{
    [GpuKernel]
    public static void ImmutableLengthKernel(ImmutableArray<int> imm, int[] output)
    {
        output[0] = imm.Length;
    }
}
