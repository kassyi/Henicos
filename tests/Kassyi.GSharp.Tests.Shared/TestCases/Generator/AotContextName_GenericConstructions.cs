using Kassyi.GSharp.Core;

namespace AotNameTest
{
    public struct Box<T> where T : unmanaged { public T Value; }

    // The native declaration is not generic, so Box<int> and Box<long> both need AotContexts.Box with their own layout.
    public static partial class K
    {
        [GpuKernel]
        public static void Run(long[] data, Box<int> first, Box<long> second)
        {
            data[ThreadIndex.X] = first.Value + second.Value;
        }
    }
}
