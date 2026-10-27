using Kassyi.GSharp.Core;
using DeviceImportStructsCase;

// Kernels that use the structs and functions of DeviceImportStructsLibrary.cs, built as another assembly
namespace DeviceImportStructsConsumerCase
{
    public static partial class StructImportKernels
    {
        [GpuKernel]
        public static void ScaledKernel(int[] data)
        {
            data[ThreadIndex.X] = new Scaled(data[ThreadIndex.X]).V;
        }

        [GpuKernel]
        public static void AlphaKernel(int[] data)
        {
            var a = new Alpha(data[ThreadIndex.X]);
            data[ThreadIndex.X] = a.Probe + a.Inner.V;
        }

        [GpuKernel]
        public static void BoxKernel(int[] data)
        {
            data[ThreadIndex.X] = LibStructs.Unbox(data[ThreadIndex.X]) + new Box<int>(1).Value;
        }

        [GpuKernel]
        public static void RangeKernel(int[] data)
        {
            var a = new Range2(1);
            var b = new Range2(2, 3);
            data[ThreadIndex.X] = a.Hi + b.Hi;
        }

        // The constructor writes data[0], which the right operand reads
        [GpuKernel]
        public static void RecorderKernel(int[] data)
        {
            data[1] = new Recorder(data, 5).V + data[0];
        }

        [GpuKernel]
        public static void BorrowKernel(int[] data)
        {
            data[ThreadIndex.X] = LibStructs.Borrow(data[ThreadIndex.X]);
        }

#if !WITHOUT_SUSPENDING_CALL
        [GpuKernel]
        public static void RelayKernel(int[] buffer, int[] result)
        {
            result[ThreadIndex.X] = LibStructs.Relay(buffer, ThreadIndex.X);
        }
#endif

        // Float3 only inside the library
        [GpuKernel]
        public static void Length3Kernel(float[] data)
        {
            data[ThreadIndex.X] = LibStructs.Length3(data[ThreadIndex.X], 1f, 2f);
        }

        // Float3 in the kernel as well
        [GpuKernel]
        public static void Dot3Kernel(float[] data)
        {
            var v = new Float3(data[ThreadIndex.X], 1f, 2f);
            data[ThreadIndex.X] = LibStructs.Dot3(v);
        }

        [GpuKernel]
        public static void ParticleKernel(float[] data)
        {
            var p = new Particle(new Float3(data[0], 0f, 0f), 2f);
            data[1] = p.Weight;
        }
    }
}
