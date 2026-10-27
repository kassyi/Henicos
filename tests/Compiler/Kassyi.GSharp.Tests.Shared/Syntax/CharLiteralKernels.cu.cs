using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that write character literals. C# reads them as UTF-16 code units; the C++ source must keep the same value,
/// whatever its own encoding makes of a character outside ASCII.
/// </summary>
public static partial class CharLiteralKernels
{
    [GpuKernel]
    public static void CharLiteralKernel(int[] output, char[] text)
    {
        output[0] = 'A';
        output[1] = '\n';
        output[2] = 'é';
        output[3] = '￿';
        output[4] = '\u00E9';
        output[5] = '\uFFFF';
        output[6] = '\'';
        output[7] = '\\';
        output[8] = '\uD800';
        output[9] = '\0';
        text[0] = 'é';
        text[1] = '\uFFFF';
    }
}
