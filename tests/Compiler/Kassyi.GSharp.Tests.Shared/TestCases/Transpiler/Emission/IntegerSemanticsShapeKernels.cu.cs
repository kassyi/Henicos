using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// C# int and long arithmetic wraps, a shift uses the low bits of its count and >>> shifts in zeros (#722); a float
// converted to an integer saturates (#724); the thread built-ins are int (#521). C++ leaves all of them undefined.
public static partial class IntegerSemanticsShapeKernels
{
    // Each int and long operator that can overflow is a Prelude call; a shift that stays an operator masks its count
    [GpuKernel]
    public static void WrappedKernel(int[] a, long[] l, uint[] u, int[] s)
    {
        int t = ThreadIndex.X;
        int x = a[t];
        long y = l[t];
        a[0] = x + 1;
        a[1] = x * x - x;
        a[2] = -x;
        a[3] = x << s[t];
        a[4] = x >> s[t];
        a[5] = x >>> s[t];
        a[6] = x << 33;
        u[0] = u[t] << s[t] + 1;
        u[1] = u[t] >>> 3;
        u[2] = u[t] >> (t > 0 ? s[t] : 40);
        l[0] = y * y + x;
        l[1] = y << s[t];
        l[2] = y >> 65;
        x += a[1];
        x <<= s[t];
        x >>= 40;
        x >>>= 1;
        u[3] >>= s[t];
        u[4] >>>= s[t];
        x++;
        a[7] = x++;
        a[8] = --x;
        a[9] = a[t]--;
        short h = (short)x;
        h += 1;
        h <<= 1;
        a[10] = h;

        // The part of a target that writes (k++) goes into a temporary once; the target is written as an expression
        int k = t;
        a[k++] += x;
        a[k++]--;
    }

    // These cannot overflow, and stay C++ operators
    [GpuKernel]
    public static void UnchangedKernel(short[] h, byte[] b, int[] a, long[] l, uint[] u)
    {
        int t = ThreadIndex.X;
        a[0] = h[t] + h[t];
        a[1] = b[t] * b[t];
        a[2] = -h[t];
        l[0] = (long)a[t] * a[t];
        l[1] = a[t] + 1L;
        u[0] = u[t] * 3u + 1u;
        a[3] = a[t] & 0xFF | a[t] ^ 1;
        a[4] = a[t] / 3 % 2;
        a[5] = 1 << 4;
        u[1] = u[t] << 4;
        short s = h[t];
        s++;
        a[6] = s;
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = i;
        }

        for (int i = t; i > 0; --i)
        {
            a[i] = 0;
        }

        for (long i = 0; i <= 100; i++)
        {
            l[i] = i;
        }
    }

    // The step of a loop counter wraps where the condition does not bound it, or something else writes the counter
    [GpuKernel]
    public static void LoopStepKernel(int[] a, int n)
    {
        for (int i = 0; i <= n; i++)
        {
            a[0] = i;
        }

        for (int i = 0; i < n; i++)
        {
            i += a[i];
        }

        for (int i = 0; i < n; i += 2)
        {
            a[i] = 0;
        }

        for (int i = 0; n > i; i++)
        {
            a[i] = 1;
        }

        for (int i = 0; i < n; i--)
        {
            a[0] = i;
        }

        // A reference to the counter, even in parentheses, may write it
        for (int i = 0; i < n; i++)
        {
            Keep(ref (i));
        }
    }

    [GpuDeviceFunction]
    private static void Keep(ref int value)
    {
        value += 0;
    }

    // A float or double converted to an integer saturates; a type narrower than int truncates the int result
    [GpuKernel]
    public static void ConversionKernel(float[] f, double[] d, int[] i, uint[] u, long[] l, ulong[] ul, byte[] b,
        short[] s)
    {
        int t = ThreadIndex.X;
        i[t] = (int)f[t];
        u[t] = (uint)d[t];
        l[t] = (long)f[t];
        ul[t] = (ulong)d[t];
        b[t] = (byte)f[t];
        s[t] = (short)(d[t] * 2.0);
        i[0] = (int)3.7f;
        f[t] = (float)d[t];
        i[1] = (int)l[t];
    }

    // The thread built-ins are int, so var and the arithmetic on them are signed
    [GpuKernel]
    public static void BuiltInKernel(int[] a)
    {
        var i = ThreadIndex.X;
        a[i] = BlockIndex.X * BlockDimension.X + i - GridDimension.Y;
    }
}
