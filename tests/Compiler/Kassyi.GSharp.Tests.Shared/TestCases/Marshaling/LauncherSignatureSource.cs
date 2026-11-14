using System;
using Kassyi.GSharp.Core;

namespace Signatures
{
    public struct Blit { public int A; }

    public struct Held { public int[] Items; }

    public static partial class Kernels
    {
        // One parameter per way a launcher passes it; overloaded, so every launcher kind is generated
        [GpuKernel]
        public static void Mixed(int[] data, float[]? optional, Memory<int>? memory, ref int count, out long total,
            in int step, Blit blit, ref Blit blitRef, ref Held held, Blit[] blits, Held[] helds, in short narrow,
            int scale = 3)
        {
            total = 0;
        }

        [GpuKernel]
        public static void Mixed(long value) { }
    }
}
