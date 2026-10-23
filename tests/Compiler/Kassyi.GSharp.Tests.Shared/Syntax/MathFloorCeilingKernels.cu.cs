using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class MathFloorCeilingKernels
{
    [GpuKernel]
    public static void FloorCeilingKernel(
        double[] doubleInputs, double[] doubleFloor, double[] doubleCeiling,
        float[] floatInputs, float[] floatFloor, float[] floatCeiling)
    {
        int tid = ThreadIndex.X;
        doubleFloor[tid] = Math.Floor(doubleInputs[tid]);
        doubleCeiling[tid] = Math.Ceiling(doubleInputs[tid]);
        floatFloor[tid] = MathF.Floor(floatInputs[tid]);
        floatCeiling[tid] = MathF.Ceiling(floatInputs[tid]);
    }

    [GpuKernel]
    public static void PowKernel(
        double[] doubleBases, double[] doubleExponents, double[] doubleResults,
        float[] floatBases, float[] floatExponents, float[] floatResults)
    {
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
