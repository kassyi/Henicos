using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// An element of a constant string is a value, or a read at run time; a constant index outside the string throws where
// it runs, which may be a branch that never runs. An element of a null constant, or foreach over it, throws for
// certain, which is GSH0003
public static partial class ConstantStringElementKernels
{
    private const string Text = "a\u00e9";

    private const string Missing = null;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConstantStringElementKernel(int[] r)
    {
        r[0] = Text[1] + "xyz"[r[1]];
        if (Text.Length > 10)
        {
            r[2] = Text[10];
        }

        r[3] = Missing[0];
        r[4] = "abc"[-1];
        foreach (char c in Missing)
        {
            r[5] = c;
        }
    }
}
