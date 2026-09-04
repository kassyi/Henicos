using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct InvalidStructWithString
{
    public string Name;
    public float[] Data;
}

public struct InvalidStructWithList
{
    public List<int> Items;
}

public static class GEN_33_SadPathKernels
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithStringStruct(InvalidStructWithString s)
    {
    }

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithListStruct(InvalidStructWithList s)
    {
    }
}
