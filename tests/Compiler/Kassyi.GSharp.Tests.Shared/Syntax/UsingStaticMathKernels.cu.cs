using Kassyi.GSharp.Core;
using static System.Math;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Math members called through <c>using static System.Math</c> map to the same native functions as <c>Math.Sqrt</c>
/// (#899), and a constant is written as its value.
/// </summary>
public static partial class UsingStaticMathKernels
{
    [GpuKernel]
    public static void UsingStaticMathKernel(double[] inputs, double[] results, int[] intInputs, int[] intResults)
    {
        int tid = ThreadIndex.X;
        results[tid] = Sqrt(Abs(inputs[tid])) + Max(inputs[tid], 1.0) + Min(inputs[tid], -1.0) + PI;
        intResults[tid] = Clamp(intInputs[tid], -5, 5);
    }
}
