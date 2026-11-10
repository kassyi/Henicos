#define CONDITIONAL_CALL_FILE_DEFINED
#undef NETCOREAPP
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The #define and #undef of this file decide the [Conditional] calls written in it, also where G# copies them: into the
// state machine of a kernel and the generated managed code, which has none of these directives
public static partial class ConditionalCallDirectiveKernels
{
    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_CALL_FILE_DEFINED")]
    public static void AddWhenFileDefined(int[] data, int i)
    {
        data[i] += 10;
    }

    // The project defines NETCOREAPP; this file undefines it
    [GpuDeviceFunction]
    [Conditional("NETCOREAPP")]
    public static void AddWhenProjectDefined(int[] data, int i)
    {
        data[i] += 1000;
    }

    // Defined only by the #define of this file: kept (+10); NETCOREAPP is undefined here: removed (no +1000)
    [GpuKernel]
    public static void FileDirectivesKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] += 1;
        AddWhenFileDefined(data, i);
        AddWhenProjectDefined(data, i);
    }

    // NETCOREAPP is undefined in this file, so the block is inactive although the project defines the symbol: +1
    [GpuKernel]
    public static void UndefinedBlockKernel(int[] data)
    {
        int i = ThreadIndex.X;
#if NETCOREAPP
        data[i] += 300;
#endif
        data[i] += 1;
    }

    // An active block ends right before the closing brace, where the #endif is trivia of the brace: +1 and +5
    [GpuKernel]
    public static void BlockAtEndKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] += 1;
#if CONDITIONAL_CALL_FILE_DEFINED
        data[i] += 5;
#endif
    }
}
