using Kassyi.GSharp.Core;

namespace AotNameTest
{
    // AotContexts drops a trailing "Context", so FooContext and Foo both map to AotContexts.Foo.
    public struct FooContext { public int A; }
    public struct Foo { public int A; }

    public static partial class K
    {
        [GpuKernel]
        public static void Run(int[] data, FooContext first, Foo second)
        {
            data[ThreadIndex.X] = first.A + second.A;
        }
    }
}
