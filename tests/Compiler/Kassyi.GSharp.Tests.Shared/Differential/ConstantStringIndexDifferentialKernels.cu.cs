using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for the elements of a constant string: C# indexes a string by UTF-16 code unit, so a non-ASCII
/// character, a surrogate pair, an embedded zero and the characters a C++ literal escapes read as C# reads them, with a
/// constant index and with an index known only at run time.
/// </summary>
public static partial class ConstantStringIndexDifferentialKernels
{
    // U+00E9, the surrogate pair of U+1F600, an embedded zero, a quote, a backslash, a question mark and a hex
    // digit after an escaped character
    public const string Text = "a\u00e9\ud83d\ude00\0\"\\?\u00e9f9";

    public const int Columns = 6;

    [GpuKernel]
    public static void StringIndexKernel(int[] index, int[] dst)
    {
        dst[0] = "\u00e9"[0];
        dst[1] = Text[2];
        dst[2] = Text[3];
        dst[3] = Text[index[0]];
        dst[4] = "x\u00e91"[index[1]];
        dst[5] = nameof(StringIndexKernel)[0] + nameof(StringIndexKernel)[index[1]];
    }
}
