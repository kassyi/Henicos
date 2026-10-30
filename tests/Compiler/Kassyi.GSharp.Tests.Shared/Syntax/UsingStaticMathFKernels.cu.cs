using Kassyi.GSharp.Core;
using static System.MathF;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// MathF members called through <c>using static System.MathF</c> map to the same native functions as
/// <c>MathF.Sqrt</c> (#899), and a constant is written as its value.
/// </summary>
public static partial class UsingStaticMathFKernels
{
    [GpuKernel]
    public static void UsingStaticMathFKernel(float[] inputs, float[] results)
    {
        int tid = ThreadIndex.X;
        results[tid] = Sqrt(Abs(inputs[tid])) + Max(inputs[tid], 1f) + Min(inputs[tid], -1f) +
                       Floor(inputs[tid]) + PI;
    }
}
