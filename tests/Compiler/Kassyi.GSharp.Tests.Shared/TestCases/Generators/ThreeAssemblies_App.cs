using Kassyi.GSharp.Core;

// The application calls the second library only
namespace XasmApp
{
    public static partial class AppKernels
    {
        [GpuKernel]
        public static void LenKernel(float[] data, int[] counts)
        {
            data[ThreadIndex.X] = XasmB.LibB.Len(data[ThreadIndex.X], 1f);
            counts[ThreadIndex.X] = XasmB.LibB.Borrow(counts[ThreadIndex.X]);
        }
    }
}
