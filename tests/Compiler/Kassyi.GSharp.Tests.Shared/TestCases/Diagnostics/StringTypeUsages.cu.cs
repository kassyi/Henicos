using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0002 for string in a body, outside a declaration's own type; not for the signature of a lambda or local function
public static partial class StringTypeUsageKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void StringTypeInBodyKernel(int[] data)
    {
        data[0] = default(string) == null ? 1 : 0;
        Func<string, int> length = (string s) => s.Length;
        static string Name() => "x";
        data[1] = length(Name());
    }
}
