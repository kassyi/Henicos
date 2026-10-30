#if GSHARP_CPU
// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A float switch with a <c>case float.NaN</c> label, flattened for its shuffle (#894 review). CPU paths only: the CUDA
/// path keeps a switch, and C++ has no switch over a float.
/// </summary>
[SkipSnapshotConsistency("CPU only (#if GSHARP_CPU); the snapshot test parses without that symbol. The lowered dispatch is the Pipeline_SwitchNaNCase snapshot")]
public static partial class SwitchNaNCaseKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // C# matches a NaN value against case float.NaN, which == never does
    [GpuKernel]
    public static void NaNCaseKernel(int[] output, float selector)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (selector)
        {
            case float.NaN:
                value = Warp.ShuffleXorSync(FullMask, value, 1);
                break;
            case 1f:
                value += 100;
                break;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
#endif
