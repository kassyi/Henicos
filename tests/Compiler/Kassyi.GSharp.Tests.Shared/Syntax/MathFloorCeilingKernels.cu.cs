using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct DoubleFloorCeilingBuffers
{
    [GpuReadOnly]
    public double[] Inputs;
    public double[] FloorResults;
    public double[] CeilingResults;
}

public struct SingleFloorCeilingBuffers
{
    [GpuReadOnly]
    public float[] Inputs;
    public float[] FloorResults;
    public float[] CeilingResults;
}

public struct DoublePowBuffers
{
    [GpuReadOnly]
    public double[] Bases;
    [GpuReadOnly]
    public double[] Exponents;
    public double[] Results;
}

public struct SinglePowBuffers
{
    [GpuReadOnly]
    public float[] Bases;
    [GpuReadOnly]
    public float[] Exponents;
    public float[] Results;
}

public static partial class MathFloorCeilingKernels
{
    [GpuKernel]
    public static void FloorCeilingKernel(
        DoubleFloorCeilingBuffers doubleBuffers,
        SingleFloorCeilingBuffers singleBuffers)
    {
        double[] doubleInputs = doubleBuffers.Inputs;
        double[] doubleFloor = doubleBuffers.FloorResults;
        double[] doubleCeiling = doubleBuffers.CeilingResults;
        float[] floatInputs = singleBuffers.Inputs;
        float[] floatFloor = singleBuffers.FloorResults;
        float[] floatCeiling = singleBuffers.CeilingResults;
        int tid = ThreadIndex.X;
        doubleFloor[tid] = Math.Floor(doubleInputs[tid]);
        doubleCeiling[tid] = Math.Ceiling(doubleInputs[tid]);
        floatFloor[tid] = MathF.Floor(floatInputs[tid]);
        floatCeiling[tid] = MathF.Ceiling(floatInputs[tid]);
    }

    [GpuKernel]
    public static void PowKernel(
        DoublePowBuffers doubleBuffers,
        SinglePowBuffers singleBuffers)
    {
        double[] doubleBases = doubleBuffers.Bases;
        double[] doubleExponents = doubleBuffers.Exponents;
        double[] doubleResults = doubleBuffers.Results;
        float[] floatBases = singleBuffers.Bases;
        float[] floatExponents = singleBuffers.Exponents;
        float[] floatResults = singleBuffers.Results;
        int tid = ThreadIndex.X;
        doubleResults[tid] = Math.Pow(doubleBases[tid], doubleExponents[tid]);
        floatResults[tid] = MathF.Pow(floatBases[tid], floatExponents[tid]);
    }

    [GpuKernel]
    public static void RoundKernel(
        double[] doubleInputs, double[] doubleResults, float[] floatInputs, float[] floatResults)
    {
        int tid = ThreadIndex.X;
        doubleResults[tid] = Math.Round(doubleInputs[tid]);
        floatResults[tid] = MathF.Round(floatInputs[tid]);
    }
}
