using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class LookalikeNoParametersKernels
    {
        // Foo generates LaunchFoo(<launch options>), and LaunchFoo() is a kernel of its own
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Foo() { }

        [GpuKernel]
        public static void LaunchFoo() { }
    }
}
