using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace ConstantLiteralCulture;

// Each value prints differently under de-DE (decimal comma) or sv-SE (U+2212 MINUS SIGN) unless it is formatted with
// the invariant culture: const field references, omitted arguments (with and without a default clause) and the
// defaults that the launcher repeats in C#, a negated literal and the loop variable of Gpu.ForUnroll.
[SkipSnapshotConsistency("Generator input of ConstantLiteralCultureTests")]
public static partial class CultureKernels
{
    public const double Half = 0.5;
    public const float Scale = 1.5f;
    public const int Negative = -5;
    public const long NegativeLong = -7L;
    public const double Tiny = -1.25e-300;

    [GpuDeviceFunction]
    public static float Offset(float x, [Optional, DefaultParameterValue(-2.5f)] float k) => x + k;

    [GpuDeviceFunction]
    public static double Shift(double x, [Optional, DefaultParameterValue(-0.75)] double k) => x + k;

    [GpuDeviceFunction]
    public static long Lower([Optional, DefaultParameterValue(-9L)] long k) => k;

    [GpuKernel]
    public static void CultureKernel(
        double[] doubles,
        float[] singles,
        long[] longs,
        float k = -1.5f,
        double h = -0.25,
        int n = -3,
        long l = -11L)
    {
        int i = ThreadIndex.X;
        doubles[i] = doubles[i] * Half + Tiny + h + Shift(doubles[i]);
        singles[i] = singles[i] * Scale + k + Offset(singles[i]);
        longs[i] = Negative + NegativeLong + n + l + Lower() + -13;
        long sum = 0;
        Gpu.ForUnroll(-3, 2, j => { sum += j; });
        longs[i] += sum;
    }
}
