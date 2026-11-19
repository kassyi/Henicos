using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// An explicit union with an arm of its own type (CS0523) as a parameter, an array parameter and a local
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class CyclicExplicitUnion
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Union
    {
        [FieldOffset(0)] public Union A;
        [FieldOffset(0)] public int B;
    }

    [GpuKernel]
    public static void UnionKernel(Union value, Union[] values, int[] output)
    {
        Union copy = value;
        output[0] = copy.B + values.Length;
    }
}
