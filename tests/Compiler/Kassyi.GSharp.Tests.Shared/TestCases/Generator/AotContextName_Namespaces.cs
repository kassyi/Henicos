using Kassyi.GSharp.Core;

namespace N1 { public struct Pair { public int A; } }
namespace N2 { public struct Pair { public int A; } }

namespace AotNameTest
{
    // N1.Pair and N2.Pair both map to AotContexts.Pair.
    public static partial class K
    {
        [GpuKernel]
        public static void Run(int[] data, N1.Pair first, N2.Pair second)
        {
            data[ThreadIndex.X] = first.A + second.A;
        }
    }
}
