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
        // nameof is written as its string
        [GpuKernel]
        public static void Run(int[] data)
        {
            Value v = default;
            data[ThreadIndex.X] = Twice(v.A) + nameof(LocalValue).Length;
        }
    }
}
