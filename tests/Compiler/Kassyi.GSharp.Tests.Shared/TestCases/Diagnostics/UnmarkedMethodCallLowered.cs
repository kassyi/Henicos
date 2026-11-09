using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace UnmarkedMethodCallLoweredTest
{
    public static class U
    {
        public static int UIter(int v) => v + 1;

        public static int UForeach(int v) => v + 2;

        public static int UGroup(int v) => v + 3;

        public static int UFunc(int v) => v + 4;
    }

    public static partial class UnmarkedLoweredKernels
    {
        [GpuDeviceFunction]
        public static IEnumerable<int> Gen(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return U.UIter(i);
            }
        }

        [GpuKernel]
        public static void IterKernel(int[] output)
        {
            foreach (var x in Gen(3))
            {
                output[0] += U.UForeach(x);
            }
        }

        [GpuKernel]
        public static void GroupKernel(int[] output)
        {
            output[0] = output.AsStackEnumerable().Select(U.UGroup).Sum();
            Func<int, int> f = U.UFunc;
            output[1] = f(1);
        }
    }
}
