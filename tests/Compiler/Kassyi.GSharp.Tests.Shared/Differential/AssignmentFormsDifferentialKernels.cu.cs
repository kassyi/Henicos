// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A plain struct for the <c>Nullable&lt;T&gt;</c> casts.</summary>
public struct DiffNullablePair
{
    public int X;
    public int Y;
}

/// <summary>A computed <see cref="int"/> property with a getter and a setter that run code (#908).</summary>
public struct DiffScaled
{
    private int _half;

    public int Value
    {
        get => _half * 2;
        set => _half = value / 2;
    }

    // A compound assignment of a computed property with the implicit receiver, inside an accessor
    public int Twice
    {
        get => Value;
        set => Value += value;
    }
}

/// <summary>A computed <see cref="float"/> property (#908).</summary>
public struct DiffCelsius
{
    private float _kelvin;

    public float Degrees
    {
        get => _kelvin - 273.15f;
        set => _kelvin = value + 273.15f;
    }
}

/// <summary>A struct that holds a struct with a computed property in a field.</summary>
public struct DiffScaledHolder
{
    public DiffScaled Inner;
    public int Tag;
}

/// <summary>
/// Differential kernels of assignment and conversion forms that the C++ paths had to spell differently from C#: the
/// floating-point <c>%</c> and <c>%=</c> (#727), the <c>Nullable&lt;T&gt;</c> casts, <c>GetValueOrDefault</c> and
/// conditionals with a <c>null</c> branch (#1143), discards as assignment targets (#1289), compound assignment,
/// increment and chained assignment of computed properties (#908), and nested and conditional deconstructions (#1102).
/// </summary>
public static partial class AssignmentFormsDifferentialKernels
{
    /// <summary>The float columns of <see cref="FloatRemainderKernel"/>.</summary>
    public static readonly string[] FloatRemainderSingleColumns =
    [
        "a % b",
        "x %= b",
        "a % 2.5f",
        "(t - 700) % b",
        "acc %= Halve(ref acc)",
        "Halve(ref acc) % acc",
        "(y %= b) value",
        "s[k++] %= b",
    ];

    /// <summary>The double columns of <see cref="FloatRemainderKernel"/>.</summary>
    public static readonly string[] FloatRemainderDoubleColumns =
    [
        "c % d",
        "y %= d",
        "a % d",
        "c % 3",
        "y %= b",
    ];

    /// <summary>The columns of <see cref="NullableConversionKernel"/>.</summary>
    public static readonly string[] NullableConversionColumns =
    [
        "GetValueOrDefault()",
        "GetValueOrDefault(-1)",
        "(int)opt + ((Pair)optPair).X",
        "(long)opt >> 1",
        "(int)optFloat",
        "optPair.GetValueOrDefault().Y",
        "GetValueOrDefault(Count(ref calls))",
        "has == 0 ? null : v * 2",
        "short? GetValueOrDefault(9)",
        "has != 0 ? optInt : null",
    ];

    /// <summary>The columns of <see cref="NullArrayKernel"/>.</summary>
    public static readonly string[] NullArrayColumns =
    [
        "int[] none = null",
        "has != 0 ? a : null",
        "x = has != 0 ? a : x",
        "Count(null)",
    ];

    /// <summary>The columns of <see cref="DiscardKernel"/>.</summary>
    public static readonly string[] DiscardColumns =
    [
        "_ = Next(ref k)",
        "(_, x) = (a, b)",
        "(_, x, _) with side effects",
        "var (_, y)",
        "(int _, var z)",
        "out _",
    ];

    /// <summary>The int columns of <see cref="ComputedPropertyKernel"/>.</summary>
    public static readonly string[] ComputedPropertyColumns =
    [
        "Value += a",
        "Value *= b",
        "Value <<= b",
        "Value %= 5",
        "Value ^= a",
        "old = Value++",
        "after Value++",
        "nv = ++Value",
        "Value--; --Value",
        "x = Value = a",
        "Id(Value = b + 1)",
        "cells[k++].Value += 5",
        "k after cells[k++]",
        "Twice = a",
        "Value += Bump(ref s)",
        "outer.Inner.Value += 3",
        "(Value += a) value",
        "Value -= Value--",
    ];

    /// <summary>The float columns of <see cref="ComputedPropertyKernel"/>.</summary>
    public static readonly string[] ComputedPropertySingleColumns =
    [
        "Degrees += 1f",
        "old = Degrees++",
        "Degrees %= 7f",
    ];

    /// <summary>The columns of <see cref="DeconstructionShapesKernel"/>.</summary>
    public static readonly string[] DeconstructionColumns =
    [
        "nested a",
        "nested b",
        "nested c",
        "conditional a",
        "conditional b",
        "nested conditional a",
        "nested conditional b",
        "nested conditional c",
        "side effects arr0",
        "side effects arr1",
        "side effects c",
        "side effects k",
        "nested discards b",
        "nested discards k",
        "conditional long",
        "conditional int",
        "var (p, (q, w))",
        "(int p2, var q2)",
    ];

    private const int FloatSingleCount = 8;
    private const int FloatDoubleCount = 5;
    private const int NullableCount = 10;
    private const int NullArrayCount = 4;
    private const int DiscardCount = 6;
    private const int ComputedCount = 18;
    private const int ComputedSingleCount = 3;
    private const int DeconstructionCount = 18;

    [GpuDeviceFunction]
    public static float Halve(ref float v)
    {
        v = v * 0.5f;
        return v;
    }

    /// <summary>The floating-point remainder in every form of #727: C# defines it as the C <c>fmod</c> result.</summary>
    [GpuKernel]
    public static void FloatRemainderKernel(float[] a, float[] b, double[] c, double[] d, float[] rf, double[] rd)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * FloatSingleCount;
        rf[o] = a[t] % b[t];

        float x = a[t];
        x %= b[t];
        rf[o + 1] = x;

        rf[o + 2] = a[t] % 2.5f;
        rf[o + 3] = (t - 700) % b[t];

        float acc = a[t];
        acc %= Halve(ref acc);
        rf[o + 4] = acc;

        float acc2 = a[t];
        rf[o + 5] = Halve(ref acc2) % acc2;

        float y = a[t];
        float z = (y %= b[t]);
        rf[o + 6] = z + y;

        float[] s = new float[2];
        s[0] = a[t];
        s[1] = 1f;
        int k = 0;
        s[k++] %= b[t];
        rf[o + 7] = s[0] + s[1] * k;

        int p = t * FloatDoubleCount;
        rd[p] = c[t] % d[t];

        double yd = c[t];
        yd %= d[t];
        rd[p + 1] = yd;

        rd[p + 2] = a[t] % d[t];
        rd[p + 3] = c[t] % 3;

        double yb = c[t];
        yb %= b[t];
        rd[p + 4] = yb;
    }

    [GpuDeviceFunction]
    public static int Count(ref int calls)
    {
        calls++;
        return -7;
    }

    /// <summary>
    /// The <c>Nullable&lt;T&gt;</c> forms of #1143: explicit casts to the underlying type and to another numeric type,
    /// <c>GetValueOrDefault</c> with and without a default, and conditionals whose other branch is <c>null</c>.
    /// </summary>
    [GpuKernel]
    public static void NullableConversionKernel(int[] has, int[] values, float[] fvalues, int[] dst)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= has.Length)
        {
            return;
        }

        int o = t * NullableCount;
        int? optInt = has[t] != 0 ? values[t] : null;
        DiffNullablePair? optPair = has[t] != 0 ? new DiffNullablePair { X = values[t], Y = 1 } : null;
        float? optFloat = has[t] != 0 ? fvalues[t] : null;

        dst[o] = optInt.GetValueOrDefault();
        dst[o + 1] = optInt.GetValueOrDefault(-1);
        dst[o + 2] = has[t] != 0 ? (int)optInt + ((DiffNullablePair)optPair).X : 0;
        dst[o + 3] = has[t] != 0 ? (int)((long)optInt >> 1) : 0;
        dst[o + 4] = has[t] != 0 ? (int)optFloat : 0;
        dst[o + 5] = optPair.GetValueOrDefault().Y;

        int calls = 0;
        dst[o + 6] = optInt.GetValueOrDefault(Count(ref calls)) + calls * 1000;

        int? doubled = has[t] == 0 ? null : values[t] * 2;
        dst[o + 7] = doubled.GetValueOrDefault(-3);

        short? narrow = has[t] != 0 ? (short)values[t] : null;
        dst[o + 8] = narrow.GetValueOrDefault((short)9);

        int? same = has[t] != 0 ? optInt : null;
        dst[o + 9] = same.GetValueOrDefault(-5);
    }

    [GpuDeviceFunction]
    public static int CountOrMinusOne(int[] values) => values == null ? -1 : values.Length;

    /// <summary>
    /// <c>null</c> converted to an array: a local initialized with it, a conditional with it in a branch, and an
    /// argument (#1143).
    /// </summary>
    [GpuKernel]
    public static void NullArrayKernel(int[] has, int[] a, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= has.Length)
        {
            return;
        }

        int o = t * NullArrayCount;
        int[] none = null;
        r[o] = none == null ? 1 : 0;

        int[] chosen = has[t] != 0 ? a : null;
        r[o + 1] = chosen == null ? -1 : chosen.Length;

        int[] x = null;
        x = has[t] != 0 ? a : x;
        r[o + 2] = x == null ? -2 : x[t];

        r[o + 3] = CountOrMinusOne(null) * 10 + CountOrMinusOne(chosen);
    }

    [GpuDeviceFunction]
    public static int Next(ref int k)
    {
        k++;
        return k * 10;
    }

    [GpuDeviceFunction]
    public static bool TryHalf(int value, out int half)
    {
        half = value / 2;
        return (value & 1) == 0;
    }

    /// <summary>A discard as the target of an assignment, a deconstruction and an <c>out</c> argument (#1289).</summary>
    [GpuKernel]
    public static void DiscardKernel(int[] a, int[] b, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * DiscardCount;
        int k = 0;
        _ = Next(ref k);
        r[o] = k;

        int x = 0;
        (_, x) = (a[t], b[t]);
        r[o + 1] = x;

        int x2 = 0;
        (_, x2, _) = (Next(ref k), b[t] + k, Next(ref k));
        r[o + 2] = x2 * 100 + k;

        var (_, y) = (Next(ref k), a[t]);
        r[o + 3] = y + k;

        (int _, var z) = (Next(ref k), b[t]);
        r[o + 4] = z - k;

        r[o + 5] = TryHalf(a[t], out _) ? 1 : 2;
    }

    [GpuDeviceFunction]
    public static int Id(int value) => value;

    [GpuDeviceFunction]
    public static int Bump(ref DiffScaled s)
    {
        s.Value = 100;
        return 1;
    }

    /// <summary>
    /// Compound assignment, increment and decrement, and chained assignment of computed properties (#908): the receiver
    /// is found once, the getter runs before the right side, the setter last, and the value is the one C# defines.
    /// </summary>
    [GpuKernel]
    public static void ComputedPropertyKernel(int[] a, int[] b, float[] f, int[] r, float[] rf)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * ComputedCount;
        int av = a[t];
        int bv = b[t];

        DiffScaled s = default;
        s.Value = av;
        s.Value += av;
        r[o] = s.Value;

        s.Value *= bv;
        r[o + 1] = s.Value;

        s.Value <<= bv;
        r[o + 2] = s.Value;

        s.Value %= 5;
        r[o + 3] = s.Value;

        s.Value ^= av;
        r[o + 4] = s.Value;

        int old = s.Value++;
        r[o + 5] = old;
        r[o + 6] = s.Value;

        int nv = ++s.Value;
        r[o + 7] = nv;

        s.Value--;
        --s.Value;
        r[o + 8] = s.Value;

        int x = s.Value = av;
        r[o + 9] = x * 3 + s.Value;

        r[o + 10] = Id(s.Value = bv + 1);

        DiffScaled[] cells = new DiffScaled[2];
        int k = 0;
        cells[k++].Value += 5;
        r[o + 11] = cells[0].Value * 10 + cells[1].Value;
        r[o + 12] = k;

        DiffScaled twice = default;
        twice.Value = 6;
        twice.Twice = av;
        r[o + 13] = twice.Value;

        DiffScaled bumped = default;
        bumped.Value = 8;
        bumped.Value += Bump(ref bumped);
        r[o + 14] = bumped.Value;

        DiffScaledHolder outer = default;
        outer.Inner.Value = bv;
        outer.Inner.Value += 3;
        r[o + 15] = outer.Inner.Value;

        DiffScaled used = default;
        r[o + 16] = (used.Value += av) + used.Value;

        DiffScaled mixed = default;
        mixed.Value = bv;
        mixed.Value -= mixed.Value--;
        r[o + 17] = mixed.Value;

        int q = t * ComputedSingleCount;
        DiffCelsius c = default;
        c.Degrees = f[t];
        c.Degrees += 1f;
        rf[q] = c.Degrees;

        float oldDegrees = c.Degrees++;
        rf[q + 1] = oldDegrees;

        c.Degrees %= 7f;
        rf[q + 2] = c.Degrees;
    }

    /// <summary>
    /// Deconstructions whose sides are nested tuples or conditionals (#1102): the targets are found left to right, then
    /// the right side is evaluated (one branch of a conditional), then the elements are assigned.
    /// </summary>
    [GpuKernel]
    public static void DeconstructionShapesKernel(int[] values, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t * 3 + 2 >= values.Length)
        {
            return;
        }

        int o = t * DeconstructionCount;
        int a = values[t * 3];
        int b = values[t * 3 + 1];
        int c = values[t * 3 + 2];

        (a, (b, c)) = (a + 1, (b ^ 3, a - 1));
        r[o] = a;
        r[o + 1] = b;
        r[o + 2] = c;

        bool f = a > 0;
        (a, b) = f ? (b, a) : (a, b);
        r[o + 3] = a;
        r[o + 4] = b;

        bool g = (b & 1) == 0;
        (a, (b, c)) = f ? (c, (a, b)) : (b, g ? (c, a) : (a, c));
        r[o + 5] = a;
        r[o + 6] = b;
        r[o + 7] = c;

        int k = 0;
        int[] arr = new int[4];
        (arr[k++], (arr[k++], c)) = (Next(ref k), (k, k * 2));
        r[o + 8] = arr[0];
        r[o + 9] = arr[1];
        r[o + 10] = c;
        r[o + 11] = k;

        (_, (b, _)) = (Next(ref k), (a + k, Next(ref k)));
        r[o + 12] = b;
        r[o + 13] = k;

        long wide = 0;
        int narrow = 0;
        (wide, narrow) = g ? (a, b) : (b, 7);
        r[o + 14] = (int)(wide >> 1);
        r[o + 15] = narrow;

        var (p, (q, w)) = (a, (b, c));
        r[o + 16] = p * 3 + q * 5 + w;

        (int p2, var q2) = g ? (a, b) : (b, a);
        r[o + 17] = p2 - q2;
    }
}
