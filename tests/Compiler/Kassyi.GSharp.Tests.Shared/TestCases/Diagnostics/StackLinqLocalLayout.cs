using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace StackLinqLocalLayoutTest
{
    public struct Named { public int Id; public string Name; }

    public static partial class StackLinqLocalKernels
    {
        [GpuKernel]
        public static void LocalQueriesKernel(int[] inputs, int[] output)
        {
            int bound = inputs[0];
            var positive = inputs.AsStackEnumerable().Where(v => v > bound);
            Span<int> span = inputs;
            var doubled = span.AsStackEnumerable().Select(v => v * 2);
            output[0] = positive.Count() + doubled.Sum();
            Named named = default;
            output[1] = named.Id;
        }
    }
}
