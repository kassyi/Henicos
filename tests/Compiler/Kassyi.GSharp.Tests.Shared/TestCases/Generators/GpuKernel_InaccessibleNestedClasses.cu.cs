using Kassyi.GSharp.Core;

namespace TestNamespace.Nested;

// Kernel classes that the module context, in another namespace, cannot name: a private, a protected and a private class
// in a private class. The public class between them keeps the order of the warmups checkable
public static partial class PrivateHost
{
    private static partial class Kernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void PrivateKernel(int[] data) => data[ThreadIndex.X] = 1;
    }
}

public static partial class PublicKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void PublicKernel(int[] data) => data[ThreadIndex.X] = 2;
}

public partial class ProtectedHost
{
    protected static partial class Kernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void ProtectedKernel(int[] data) => data[ThreadIndex.X] = 3;
    }
}

internal static partial class TwoLevelHost
{
    private static partial class Middle
    {
        // Two private classes of one container with kernels of the same name each get a relay of their own
        private static partial class Kernels
        {
            [GpuKernel]
            [SkipSnapshotConsistency("Generator case")]
            public static void DeepKernel(int[] data) => data[ThreadIndex.X] = 4;
        }

        private static partial class OtherKernels
        {
            [GpuKernel]
            [SkipSnapshotConsistency("Generator case")]
            public static void DeepKernel(int[] data) => data[ThreadIndex.X] = 5;
        }
    }
}
