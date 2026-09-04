using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.GeneratorTestCode;

public struct ChildStruct
{
    public float[] Values;
    public int Scale;
}

public struct ParentStruct
{
    public ChildStruct Child;
    public float[] ExtraBuffer;
}

public struct StructWithArrayOfStructs
{
    public ChildStruct[] Children;
    public int Count;
}

public static class GEN_33_TestKernels
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithArrayStruct(ParentStruct parent)
    {
    }

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithStructArray(StructWithArrayOfStructs container)
    {
    }
}
