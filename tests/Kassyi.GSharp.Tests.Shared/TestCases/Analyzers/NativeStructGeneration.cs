using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;
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

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PackedShadowStruct
{
    public float[] Data;
    public byte Flag;
}

public static partial class GEN_33_TestKernels
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

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithPackedShadowStruct(PackedShadowStruct data)
    {
    }
}
