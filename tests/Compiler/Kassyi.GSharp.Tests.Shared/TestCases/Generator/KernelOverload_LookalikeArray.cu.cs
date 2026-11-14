using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class LookalikeArrayKernels
    {
        // Foo generates LaunchFoo(int[], <launch options>), and LaunchFoo(int[]) is a kernel of its own: the parameter
        // lists differ by the launch options, so C# keeps both
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Foo(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void LaunchFoo(int[] data) { data[ThreadIndex.X] = 2; }
    }
}
