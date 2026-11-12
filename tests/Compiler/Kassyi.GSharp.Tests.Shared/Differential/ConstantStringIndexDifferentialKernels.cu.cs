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

    /// <summary>
    /// A constant index outside a constant string is correct C#: in a branch that never runs it does nothing, and
    /// where it runs it throws <see cref="System.IndexOutOfRangeException"/>. <c>mode[0]</c>: 1 reads one past the
    /// end of a three-element string.
    /// </summary>
    [GpuKernel]
    public static void ConstantIndexOutsideKernel(int[] mode, int[] dst)
    {
        dst[0] = 1;
        if (Text.Length > 20)
        {
            dst[1] = Text[20];
        }

        if (mode[0] == 1)
        {
            dst[1] = "abc"[3];
        }

        dst[2] = 100;
    }

    /// <summary>The length of <c>dst</c> of <see cref="ForEachKernel"/>: one more than it writes.</summary>
    public const int ForEachColumns = 17;

    /// <summary>
    /// <c>foreach</c> over a constant string reads its UTF-16 code units, the surrogates and the embedded zero
    /// included, and nothing after the last one; an explicit element conversion keeps the low byte.
    /// </summary>
    [GpuKernel]
    public static void ForEachKernel(int[] dst)
    {
        int i = 0;
        foreach (char c in Text)
        {
            dst[i++] = c;
        }

        foreach (var c in "x\u00e9")
        {
            dst[i++] = c;
        }

        foreach (byte b in "\u00e9\u0141")
        {
            dst[i++] = b;
        }

        dst[i] = 100 + i;
    }
}
