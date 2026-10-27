using Kassyi.GSharp.Core;
using DeviceExportCase;

// Kernels that call the device functions of DeviceExportLibrary.cs, built as another assembly
namespace DeviceImportCase
{
    public static partial class ImportKernels
    {
        // Twice calls the internal Helper of the library
        [GpuKernel]
        public static void TwiceKernel(int[] data)
        {
            data[ThreadIndex.X] = LibMath.Twice(data[ThreadIndex.X]);
        }

        // The second overload of Scale (_o1)
        [GpuKernel]
        public static void OverloadKernel(float[] data)
        {
            data[ThreadIndex.X] = LibMath.Scale(data[ThreadIndex.X]);
        }

        // The constructor and the computed property of a library struct
        [GpuKernel]
        public static void StructKernel(float[] data)
        {
            var v = new Vec2(data[ThreadIndex.X], 2f);
            data[ThreadIndex.X] = v.Sum;
        }

#if !WITHOUT_SUSPENDING_CALL
        // Reaches barriers inside the library: on the CPU dialects the call suspends the kernel
        [GpuKernel]
        public static void ExchangeKernel(int[] buffer, int[] result)
        {
            int tid = ThreadIndex.X;
            result[tid] = LibMath.Exchange(buffer, tid, tid * 10);
        }
#endif

        // Reaches dynamic shared memory inside the library: on CUDA the kernel owns the table and passes it
        [GpuKernel]
        public static void ScratchKernel(int[] data, int n)
        {
            data[ThreadIndex.X] = LibMath.Scratch(n, data[ThreadIndex.X]);
        }

        // ReadAndClear writes data[0], which the right operand reads: C# order needs a sequence; TakeOne writes only x
        [GpuKernel]
        public static void EffectKernel(int[] data)
        {
            int x = data[1];
            data[2] = LibMath.ReadAndClear(ref x, data) + data[0];
            int y = data[1];
            data[3] = LibMath.TakeOne(ref y) + data[0];
        }
    }
}
