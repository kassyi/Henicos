using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// An element of a constant string that C# reads for certain is a value; a constant index outside the string and an
// element of a null constant throw for certain, which is GSH0003
public static partial class ConstantStringElementKernels
{
    private const string Text = "a\u00e9";

    private const string Missing = null;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConstantStringElementKernel(int[] r)
    {
        r[0] = Text[1] + "xyz"[r[1]];
        r[2] = Text[2];
        r[3] = Missing[0];
        r[4] = "abc"[-1];
    }
}
