#nullable enable
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffInitInner
{
    public int A;
    public int B;
}

public struct DiffInitOuter
{
    public DiffInitInner I;
    public DiffInitInner J;
    public int K;
}

public struct DiffInitDeep
{
    public DiffInitOuter O;
    public int Tag;
}

public struct DiffInitWithConstructor
{
    public DiffInitInner I;
    public int K;

    public DiffInitWithConstructor(int seed)
    {
        I.A = seed;
        I.B = seed * 2;
        K = seed * 3;
    }
}

/// <summary>
/// Differential kernels for expression forms whose C++ needs the conversions or the shape C# gives them: switch
/// expressions whose arms have different types and switch expressions over <see cref="float"/> and
/// <see cref="double"/>, the null-forgiving operator and <c>??</c> / <c>??=</c> on arrays, implicitly converted
/// arguments of brace-initialized constructors and array initializers, and nested member initializers.
/// </summary>
public static partial class ExpressionEmissionDifferentialKernels
{
    // -------------------------------------------------------------------------
    // 1. Switch expression arms of different types convert to the natural type of the switch expression
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SwitchArmConversionKernel(int[] a, long[] dst, float[] fdst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int col = t * 4;

        // Col 0: int and long arms: long
        dst[col + 0] = (v & 3) switch
        {
            0 => 1,
            1 => -1,
            2 => int.MaxValue,
            _ => 2L * v
        };

        // Col 1: uint and int arms: long in C#, where C++ would pick unsigned int
        dst[col + 1] = (v & 3) switch
        {
            0 => 1u,
            1 => (uint)v,
            2 => -1,
            _ => v
        };

        // Col 2: nested switch expressions whose inner arms are short, byte and long
        short s = (short)v;
        byte b = (byte)v;
        dst[col + 2] = (v & 1) switch
        {
            0 => (v & 2) switch
            {
                0 => s,
                _ => b
            },
            _ => (v & 4) switch
            {
                0 => 7L,
                _ => s
            }
        };

        // Col 3: a switch expression in an operand, whose int and long arms make the operand long
        dst[col + 3] = 3 * ((v & 1) switch
        {
            0 => v,
            _ => long.MaxValue / 4
        });

        // Float columns: int, float and long arms convert to float; int and double arms to double
        fdst[col + 0] = (v & 3) switch
        {
            0 => 1,
            1 => 2.5f,
            2 => v,
            _ => (long)v * 3
        };
        double d = (v & 1) switch
        {
            0 => v,
            _ => 0.25
        };
        fdst[col + 1] = (float)d;
        fdst[col + 2] = (v & 1) switch
        {
            0 => (char)(v & 0x7F),
            _ => -0.0f
        };
        fdst[col + 3] = (v & 3) switch
        {
            0 => s,
            1 => b,
            _ => float.NaN
        };
    }

    // -------------------------------------------------------------------------
    // 2. Switch expressions over float and double: C# equality, so NaN matches only a NaN constant and 0.0 matches -0.0
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void FloatingSwitchKernel(double[] d, float[] f, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        double x = d[t];
        float y = f[t];
        int col = t * 4;

        // Col 0: double constants, special values included
        dst[col + 0] = x switch
        {
            0.5 => 1,
            1.5 => 2,
            0.0 => 3,
            double.NaN => 4,
            double.PositiveInfinity => 5,
            double.NegativeInfinity => 6,
            double.MaxValue => 7,
            double.Epsilon => 8,
            _ => 0
        };

        // Col 1: float constants, without a NaN arm (NaN takes the discard)
        dst[col + 1] = y switch
        {
            0.5f => 1,
            -2.5f => 2,
            0f => 3,
            float.PositiveInfinity => 4,
            float.MinValue => 5,
            _ => 0
        };

        // Col 2: the governing expression is evaluated once
        double g = x;
        int matched = (g += 1.0) switch
        {
            1.5 => 10,
            2.5 => 20,
            1.0 => 30,
            _ => 40
        };
        dst[col + 2] = matched + (g == x + 1.0 ? 100 : 0);

        // Col 3: floating switch expressions nested in each other, with float NaN arms
        dst[col + 3] = x switch
        {
            0.5 => y switch
            {
                0.5f => 11,
                _ => 12
            },
            _ => y switch
            {
                0f => 13,
                float.NaN => 14,
                _ => 15
            }
        };
    }

    // -------------------------------------------------------------------------
    // 2b. Switch expressions with null arms and Nullable<T> inputs
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NullableSwitchKernel(int[] a, double[] d, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int col = t * 4;

        // Col 0: int, short, null and default arms of a switch expression target-typed to int?
        int? r = (v & 3) switch
        {
            0 => 1,
            1 => null,
            2 => (short)v,
            _ => default
        };
        dst[col + 0] = r ?? -1;

        // Col 1: a Nullable<int> input with a null arm
        int? m = default;
        if ((v & 4) != 0)
        {
            m = v & 3;
        }

        dst[col + 1] = m switch
        {
            1 => 10,
            null => 20,
            3 => 30,
            _ => 40
        };

        // Col 2: a Nullable<double> input with NaN, zero and null arms
        double? x = default;
        if ((v & 1) != 0)
        {
            x = d[t];
        }

        dst[col + 2] = x switch
        {
            double.NaN => 1,
            0.0 => 2,
            null => 3,
            0.5 => 4,
            _ => 5
        };

        // Col 3: an array arm and a null arm
        int[]? picked = (v & 8) switch
        {
            0 => a,
            _ => null
        };
        dst[col + 3] = picked == null ? -1 : picked[t];
    }

    // -------------------------------------------------------------------------
    // 3. The null-forgiving operator has no run-time meaning; ?? and ??= on arrays test the array for null
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NullForgivingAndArrayCoalesceKernel(int[] a, int[] b, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int col = t * 4;

        // Col 0: ! on an element access, a member access and an argument
        dst[col + 0] = a![t] + a!.Length * 1000 + ElementOf(b!, t);

        // Col 1: ?? on arrays evaluates its right side only when the left side is null
        int[]? pick = default;
        if ((a[t] & 1) == 0)
        {
            pick = a;
        }

        int rightCount = 0;
        int[] chosen = pick ?? (rightCount++ >= 0 ? b : a);
        dst[col + 1] = chosen[t] + rightCount * 1000;

        // Col 2: ??= on an array local assigns only while it is null
        int[]? target = default;
        if ((a[t] & 2) != 0)
        {
            target = b;
        }

        int assignCount = 0;
        target ??= assignCount++ >= 0 ? a : b;
        target ??= assignCount++ >= 0 ? b : a;
        dst[col + 2] = target[t] + assignCount * 1000;

        // Col 3: ?? in a member access and an element access, and the value of ??=
        int[]? other = default;
        if ((a[t] & 4) != 0)
        {
            other = a;
        }

        int[] assigned = other ??= b;
        dst[col + 3] = (pick ?? b).Length + (pick ?? b)[t] * 10 + assigned[t] * 100;
    }

    [GpuDeviceFunction]
    private static int ElementOf(int[] values, int index) => values[index];

    // -------------------------------------------------------------------------
    // 4. Arguments that C# converts implicitly reach brace-initialized constructors and array initializers converted
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void ImplicitArgumentConversionKernel(int[] a, long[] la, float[] dst, double[] ddst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        long l = la[t];
        int col = t * 4;

        var v2 = new Vector2(1.5f, v);
        var v3 = new Vector3(v, v + 1, (short)v);
        var v4 = new Vector4(l, 2, 'A', 3.5f);
        float[] w = { v, 2 * v };
        dst[col + 0] = v2.X + v2.Y;
        dst[col + 1] = v3.X * 0.5f + v3.Y + v3.Z;
        dst[col + 2] = v4.X + v4.Y + v4.Z + v4.W;
        dst[col + 3] = w[0] - w[1];

        double? nd = new double?(v);
        double? nl = new double?(l);
        ddst[col + 0] = nd.Value;
        ddst[col + 1] = nl.Value;
        ddst[col + 2] = new Vector2(l, v).X;
        ddst[col + 3] = new Vector3(v) { Y = l }.Y;
    }

    // -------------------------------------------------------------------------
    // 5. A nested member initializer assigns the members of the existing member, in the written order
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NestedMemberInitializerKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int v = a[t];
        int order = 0;
        int col = t * 4;

        var outer = new DiffInitOuter
        {
            K = v,
            I = { B = v ^ 0x55 },
            J = { A = ++order * 10, B = ++order * 10 + 1 }
        };
        dst[col + 0] = outer.I.A * 1000 + outer.I.B + outer.K * 3;
        dst[col + 1] = outer.J.A * 100 + outer.J.B + order;

        var deep = new DiffInitDeep
        {
            O = { I = { A = v & 0xFFFF }, K = 3 },
            Tag = 9
        };
        dst[col + 2] = deep.O.I.A + deep.O.I.B * 7 + deep.O.K * 11 + deep.Tag * 13 + deep.O.J.A;

        // The constructor sets I first; the nested initializer changes only I.B
        var constructed = new DiffInitWithConstructor(v & 0xFF) { I = { B = 5 } };
        dst[col + 3] = constructed.I.A * 1000 + constructed.I.B * 10 + constructed.K;
    }
}
