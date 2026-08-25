namespace Kassyi.GSharp.Stdlib.Math;

public class GpuBigIntMatrix2x2 : IDisposable
{
    public GpuBigInteger M00
    {
        get;
    }
    public GpuBigInteger M01
    {
        get;
    }
    public GpuBigInteger M10
    {
        get;
    }
    public GpuBigInteger M11
    {
        get;
    }
    public bool IsOddStep
    {
        get;
    }
    public GpuContext Context
    {
        get;
    }

    public GpuBigIntMatrix2x2(GpuContext context, int numWords, bool isOddStep)
    {
        Context = context;
        IsOddStep = isOddStep;
        M00 = new GpuBigInteger(context, numWords);
        M01 = new GpuBigInteger(context, numWords);
        M10 = new GpuBigInteger(context, numWords);
        M11 = new GpuBigInteger(context, numWords);
    }

    public static GpuBigIntMatrix2x2 Identity(GpuContext context, int numWords)
    {
        var mat = new GpuBigIntMatrix2x2(context, numWords, false);
        var one = new uint[numWords];
        one[0] = 1;
        context.Provider.CopyHostToDevice(mat.M00.DevicePointer, one, numWords);
        context.Provider.CopyHostToDevice(mat.M11.DevicePointer, one, numWords);
        // M01 and M10 are initialized to 0 by default cuMemAlloc, but let's be safe
        var zero = new uint[numWords];
        context.Provider.CopyHostToDevice(mat.M01.DevicePointer, zero, numWords);
        context.Provider.CopyHostToDevice(mat.M10.DevicePointer, zero, numWords);
        return mat;
    }

    public static GpuBigIntMatrix2x2 Multiply(GpuBigIntMatrix2x2 v, GpuBigIntMatrix2x2 u)
    {
        var ctx = v.Context;
        var outWords = v.M00.NumWords + u.M00.NumWords;
        var result = new GpuBigIntMatrix2x2(ctx, outWords, v.IsOddStep ^ u.IsOddStep);

        using var temp1 = new GpuBigInteger(ctx, outWords);
        using var temp2 = new GpuBigInteger(ctx, outWords);

        // M00 = v00 * u00 + v01 * u10
        GpuBigInteger.Mul(v.M00, u.M00, temp1);
        GpuBigInteger.Mul(v.M01, u.M10, temp2);
        GpuBigInteger.Add(temp1, temp2, result.M00);

        // M01 = v00 * u01 + v01 * u11
        GpuBigInteger.Mul(v.M00, u.M01, temp1);
        GpuBigInteger.Mul(v.M01, u.M11, temp2);
        GpuBigInteger.Add(temp1, temp2, result.M01);

        // M10 = v10 * u00 + v11 * u10
        GpuBigInteger.Mul(v.M10, u.M00, temp1);
        GpuBigInteger.Mul(v.M11, u.M10, temp2);
        GpuBigInteger.Add(temp1, temp2, result.M10);

        // M11 = v10 * u01 + v11 * u11
        GpuBigInteger.Mul(v.M10, u.M01, temp1);
        GpuBigInteger.Mul(v.M11, u.M11, temp2);
        GpuBigInteger.Add(temp1, temp2, result.M11);

        return result;
    }

    public void Apply(GpuBigInteger a, GpuBigInteger b, GpuBigInteger outA, GpuBigInteger outB)
    {
        var ctx = Context;
        var tempWords = M00.NumWords + System.Math.Max(a.NumWords, b.NumWords);

        using var tempA1 = new GpuBigInteger(ctx, tempWords);
        using var tempA2 = new GpuBigInteger(ctx, tempWords);
        using var tempB1 = new GpuBigInteger(ctx, tempWords);
        using var tempB2 = new GpuBigInteger(ctx, tempWords);

        GpuBigInteger.Mul(M00, a, tempA1);
        GpuBigInteger.Mul(M01, b, tempA2);

        GpuBigInteger.Mul(M10, a, tempB1);
        GpuBigInteger.Mul(M11, b, tempB2);

        using var resA = new GpuBigInteger(ctx, tempWords);
        using var resB = new GpuBigInteger(ctx, tempWords);

        if (!IsOddStep)
        {
            // A' = U00*A - U01*B
            // B' = U11*B - U10*A
            GpuBigInteger.Sub(tempA1, tempA2, resA);
            GpuBigInteger.Sub(tempB2, tempB1, resB);
        }
        else
        {
            // A' = U01*B - U00*A
            // B' = U10*A - U11*B
            GpuBigInteger.Sub(tempA2, tempA1, resA);
            GpuBigInteger.Sub(tempB1, tempB2, resB);
        }

        // Output size might be smaller than tempWords (because values decrease)
        // We just clear outA and outB and copy
        var zeroA = new uint[outA.NumWords];
        ctx.Provider.CopyHostToDevice(outA.DevicePointer, zeroA, outA.NumWords);
        outA.CopyFrom(resA, numWords: System.Math.Min(outA.NumWords, tempWords));

        var zeroB = new uint[outB.NumWords];
        ctx.Provider.CopyHostToDevice(outB.DevicePointer, zeroB, outB.NumWords);
        outB.CopyFrom(resB, numWords: System.Math.Min(outB.NumWords, tempWords));
    }

    public void Dispose()
    {
        M00.Dispose();
        M01.Dispose();
        M10.Dispose();
        M11.Dispose();
    }
}
