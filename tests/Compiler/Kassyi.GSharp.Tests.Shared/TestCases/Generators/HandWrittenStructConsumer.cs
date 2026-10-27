using Kassyi.GSharp.Core;
using XasmLib;

// Uses the hand-written Alpha as a type only: its field, no constructor or computed property
namespace XasmConsumer
{
    public static partial class AlphaKernels
    {
        [GpuKernel]
        public static void FieldKernel(Alpha[] values, int[] result)
        {
            result[ThreadIndex.X] = values[ThreadIndex.X].V;
        }
    }
}
