using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Another part of the class whose other file defines CONDITIONAL_CALL_FILE_DEFINED for a kept call. The symbol is not
// defined in this file, so its block stays inactive, also in the generated managed file that defines it for that call
public static partial class ConditionalCallDirectiveKernels
{
    [GpuKernel]
    public static void OtherPartBlockKernel(int[] data)
    {
        int i = ThreadIndex.X;
#if CONDITIONAL_CALL_FILE_DEFINED
        data[i] += 1000;
#endif
        data[i] += 1;
    }
}
