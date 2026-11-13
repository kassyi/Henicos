using Kassyi.GSharp.Core;
using static FileLocalDirectives.LocalMath;
using Value = FileLocalDirectives.LocalValue;

namespace FileLocalDirectives
{
    file struct LocalValue
    {
        public int A;
    }

    file static partial class LocalMath
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;
    }

    public static partial class DirectiveKernels
    {
        // Names that the user's directives resolve: a static member through using static, a type through an alias;
        // nameof is written as its string. The kernel waits at a barrier, so the managed path copies it
        [GpuKernel]
        public static void Run(int[] data)
        {
            Value v = default;
            Gpu.SyncThreads();
            data[ThreadIndex.X] = Twice(v.A) + nameof(LocalValue).Length;
        }
    }

    public static partial class CustomNameofKernels
    {
        // A method named nameof is an ordinary call, whose arguments name what they name
        [GpuDeviceFunction]
        public static int nameof(int x) => x;

        [GpuKernel]
        public static void Run(int[] data)
        {
            Gpu.SyncThreads();
            data[ThreadIndex.X] = nameof(Twice(1));
        }
    }
}
