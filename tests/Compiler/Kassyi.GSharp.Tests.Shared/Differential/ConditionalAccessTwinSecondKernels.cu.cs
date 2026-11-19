#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessTwinB;

// The second file of ConditionalAccessTwinKernels (ConditionalAccessTwinFirstKernels.cu.cs)
namespace Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessTwinB
{
    internal static partial class TwinBExtensions
    {
        [GpuDeviceFunction]
        public static int TwinSum(this int[] values, int k) => values[1] + 2000;

        [GpuDeviceFunction]
        public static int TwinSame(this int[] values) => values[3] + 4000;
    }
}

namespace Kassyi.GSharp.Tests.Shared.Differential
{
    public static partial class ConditionalAccessTwinKernels
    {
        // C# binds TwinBExtensions.TwinSum(int[], int) and TwinBExtensions.TwinSame: this file imports only
        // ConditionalAccessTwinB
        [GpuKernel]
        public static void TwinSecondFileKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? s = (a[t] & 1) == 0 ? null : a;
            Gpu.SyncThreads();

            int col = t * 2;
            dst[col + 0] = s?.TwinSum(1) ?? -1;
            dst[col + 1] = s?.TwinSame() ?? -1;
        }
    }
}
