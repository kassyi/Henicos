// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Floating-point divisions whose divisor is a constant zero. C# folds them to an infinity or NaN; the native CPU build
/// must not write the division as an operator on two constants (MSVC C2124).
/// </summary>
public static partial class ConstZeroDivisorKernels
{
    public const float NegZero = -0f;
    public const double PosZero = 0.0;

    [GpuKernel]
    public static void ClassConstZeroDivisorKernel(double[] doubles, float[] floats)
    {
        int tid = ThreadIndex.X;
        doubles[tid * 4 + 0] = 1.0 / NegZero;
        doubles[tid * 4 + 1] = 1.0 / PosZero;
        doubles[tid * 4 + 2] = 0.0 / PosZero;
        doubles[tid * 4 + 3] = -1.0 / (PosZero * 2);
        floats[tid * 2 + 0] = 1f / NegZero;
        floats[tid * 2 + 1] = (float)(1.0 / NegZero);
    }

    [GpuKernel]
    public static void LocalConstZeroDivisorKernel(double[] doubles, float[] floats)
    {
        const float LocalNegZero = -0f;
        const double LocalPosZero = 0.0;
        int tid = ThreadIndex.X;
        doubles[tid * 4 + 0] = 1.0 / LocalNegZero;
        doubles[tid * 4 + 1] = 1.0 / LocalPosZero;
        doubles[tid * 4 + 2] = 0.0 / LocalPosZero;
        doubles[tid * 4 + 3] = -1.0 / (LocalPosZero * 2);
        floats[tid * 2 + 0] = 1f / LocalNegZero;
        floats[tid * 2 + 1] = (float)(1.0 / LocalNegZero);
    }

    // The dividend is not a constant, so C# does not fold the division and the operator stays in the output
    [GpuKernel]
    public static void NonConstantDividendKernel(double[] x, double[] divided, double[] assigned)
    {
        int tid = ThreadIndex.X;
        divided[tid] = x[tid] / NegZero;
        assigned[tid] = x[tid];
        assigned[tid] /= PosZero;
    }
}
