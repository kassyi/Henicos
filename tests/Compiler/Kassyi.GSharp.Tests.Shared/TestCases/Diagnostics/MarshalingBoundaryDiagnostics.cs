#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

[StructLayout(LayoutKind.Explicit)]
public struct PartialOverlapStruct
{
    [FieldOffset(0)]
    public Vector3 A;

    [FieldOffset(8)]
    public Vector2 B;
}

[StructLayout(LayoutKind.Explicit)]
public struct MisalignedOffsetStruct
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public int B;
}

public struct ReferenceFieldStruct
{
    public string Name;
}

public struct DecimalFieldStruct
{
    public decimal Price;
}

public struct DeepStructForReturn
{
    public int[] Data;
    public int Tag;
}

[StructLayout(LayoutKind.Auto)]
public struct AutoLayoutBoundaryStruct
{
    public byte B;
    public long L;
}

public static partial class MarshalingBoundaryDiagnostics
{
    // Multidimensional array local variable -> GSH0003
    [GpuKernel]
    public static void MultidimensionalLocalKernel(int[] output)
    {
        int[,] grid2D = new int[2, 2];
        output[0] = grid2D.Length;
    }

    // LayoutKind.Explicit partial overlap, misaligned field, and reference field -> GSH0078
    [GpuKernel]
    public static void ExplicitLayoutInvalidKernel(
        PartialOverlapStruct overlap,
        MisalignedOffsetStruct misaligned,
        ReferenceFieldStruct refField)
    {
    }

    // Non-unmanaged Deep Struct return type and multidimensional array return type -> GSH0004
    [GpuKernel]
    public static DeepStructForReturn DeepStructReturnKernel(int[] input)
    {
        return default;
    }

    [GpuKernel]
    public static int[,] MultidimensionalReturnKernel(int[] input)
    {
#pragma warning disable CS8603
        return default;
#pragma warning restore CS8603
    }

    // Field type without device representation (decimal) -> GSH0079
    [GpuKernel]
    public static void DecimalFieldStructKernel(DecimalFieldStruct item)
    {
    }

    // LayoutKind.Auto array/return -> GSH0080 warning; DeepStruct array -> GSH0080 (CPU) / GSH0080 + GSH0081 (CUDA)
    [GpuKernel]
    public static AutoLayoutBoundaryStruct AutoLayoutAndDeepArrayWarningsKernel(
        AutoLayoutBoundaryStruct[] autoArr,
        DeepStructForReturn[] deepArr)
    {
        return autoArr[0];
    }
}
