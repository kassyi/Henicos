using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class LookalikePointerKernels
    {
        // Foo generates LaunchFooPtr(IntPtr, <launch options>) for its ref parameter, and LaunchFooPtr(nint) is a
        // kernel of its own
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Foo(ref int value) { value = 1; }

        [GpuKernel]
        public static void LaunchFooPtr(nint address) { }
    }
}
