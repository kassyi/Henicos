using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public struct LayoutReproVecHolder
{
    public float A;
    public Vector2 V;
    public float B;
    public Vector4 W;
}

public struct LayoutReproCoreHolder
{
    public float A;
    public Float2 V;
    public float B;
    public Float4 W;
}

public struct LayoutReproElem
{
    public byte A;
    public Float2 V;
}

[InlineArray(2)]
public struct LayoutReproElems
{
    private LayoutReproElem _element0;
}

public struct LayoutReproInlineOuter
{
    public int Tag;
    public LayoutReproElems Items;
    public int C;
}

public struct LayoutReproBag
{
    public int N;
    public int[] Data;
}

public struct LayoutReproTwoBools
{
    public bool F;
    public bool G;
    public int X;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LayoutReproPacked
{
    public byte B;
    public int I;
    public bool F;
}

public struct LayoutReproNested
{
    public LayoutReproTwoBools T;
    public char C;
    public int Y;
}

public struct LayoutReproInnerBag
{
    public long Tag;
    public int[] Items;
}

public struct LayoutReproElemHolder
{
    public int N;
    public LayoutReproInnerBag[] Elems;
}

public struct LayoutReproPair
{
    public int[] A;
    public int[] B;
}

public struct LayoutReproCharInt
{
    public char C;
    public int X;
}

public struct LayoutReproBox<T> where T : unmanaged
{
    public T V;
    public int Tag;
}

public struct LayoutReproNullableInt
{
    public int? V;
    public int X;
}

[StructLayout(LayoutKind.Explicit)]
public struct LayoutReproIntFloatUnion
{
    [FieldOffset(0)]
    public int I;

    [FieldOffset(0)]
    public float F;
}

[StructLayout(LayoutKind.Auto)]
public struct LayoutReproAutoLayout
{
    public byte A;
    public long B;
    public byte C;
}

public struct LayoutReproWithArr
{
    public int[] Data;
}

[InlineArray(2)]
public struct LayoutReproArrPair
{
    private LayoutReproWithArr _element0;
}

public struct LayoutReproArrOuter
{
    public LayoutReproArrPair P;
}

public static partial class StructLayoutRegressionKernels
{
    // R1: array of structs holding System.Numerics vectors (#498)
    [GpuKernel]
    public static void VecArrayKernel(LayoutReproVecHolder[] h, float[] o)
    {
        o[0] = h[0].V.Y;
        o[1] = h[0].W.Z;
        o[2] = h[1].A;
        unsafe
        {
            LayoutReproVecHolder s = default;
            LayoutReproVecHolder* p = &s;
            o[3] = (float)((byte*)(p + 1) - (byte*)p);
            o[4] = (float)((byte*)&s.W - (byte*)p);
        }

        h[0].B = 42f;
    }

    // R2: array of structs holding Kassyi.GSharp.Core vectors (#498)
    [GpuKernel]
    public static void CoreArrayKernel(LayoutReproCoreHolder[] h, float[] o)
    {
        o[0] = h[0].V.Y;
        o[1] = h[0].W.Z;
        o[2] = h[1].A;
        unsafe
        {
            LayoutReproCoreHolder s = default;
            LayoutReproCoreHolder* p = &s;
            o[3] = (float)((byte*)(p + 1) - (byte*)p);
            o[4] = (float)((byte*)&s.W - (byte*)p);
        }
    }

    // R3: [InlineArray] whose element has a different layout on the device (S-26)
    [GpuKernel]
    public static void InlineElemKernel(ref LayoutReproInlineOuter x, float[] o)
    {
        o[0] = x.Items[1].V.Y;
        o[1] = x.Items[1].A;
        o[2] = x.C;
        unsafe
        {
            LayoutReproInlineOuter s = default;
            LayoutReproInlineOuter* p = &s;
            o[3] = (float)((byte*)(p + 1) - (byte*)p);
            o[4] = (float)((byte*)&s.C - (byte*)p);
        }

        x.Items[1].V = new Float2(9f, 10f);
        x.C = x.C + 1;
    }

    // R4a: struct with an array field passed by value (#511)
    [GpuKernel]
    public static void BagByValKernel(LayoutReproBag b, int[] o)
    {
        o[0] = b.N;
        o[1] = b.Data.Length;
        o[2] = b.Data[1];
    }

    // R4b: struct with an array field passed by ref (#511)
    [GpuKernel]
    public static void BagByRefKernel(ref LayoutReproBag b, int[] o)
    {
        o[0] = b.N;
        o[1] = b.Data.Length;
        o[2] = b.Data[1];
        b.Data[1] = 99;
        b.N = 7;
    }

    // R4d: a null or empty array field
    [GpuKernel]
    public static void BagLengthKernel(LayoutReproBag b, int[] o)
    {
        o[0] = b.N;
        o[1] = b.Data.Length;
    }

    // R4f: an array field whose elements are converted (they hold arrays) inside a struct passed by value: the
    // kernel writes a value field of an element, which the caller sees in its array as C# would
    [GpuKernel]
    public static void ElemWriteKernel(LayoutReproElemHolder h)
    {
        h.Elems[0].Tag = 9;
        h.Elems[0].Items[0] = 5;
    }

    // R4h: an array of structs whose array field holds converted elements (with arrays of their own): the kernel
    // writes a value field of the outer element and of an inner element
    [GpuKernel]
    public static void ElemHolderArrayKernel(LayoutReproElemHolder[] hs)
    {
        hs[0].N = 4;
        hs[0].Elems[0].Tag = 9;
        hs[0].Elems[0].Items[0] = 5;
    }

    // R4g: a construct the CPU dialect cannot run (inline PTX, GSH0054 where analyzers run), which the native code
    // reports as an error after the marshaling; the PTX is a comment, so CUDA runs it as nothing
    [GpuKernel]
    public static void BagNativeErrorKernel(LayoutReproBag b, int[] o)
    {
        o[0] = b.Data[0];
        Gpu.InlinePtx("/* reported as an error on the CPU dialect */");
    }

    // R4e: two array fields that hold one array
    [GpuKernel]
    public static void AliasKernel(LayoutReproPair p)
    {
        p.A[0] = 1;
        p.B[1] = p.A[0] + 1;
    }

    // R4c: array of structs with an array field (#514)
    [GpuKernel]
    public static void BagArrayKernel(LayoutReproBag[] b, int[] o)
    {
        o[0] = b[0].N;
        o[1] = b[0].Data.Length;
        o[2] = b[1].N;
    }

    // R5a: struct with multiple bool fields passed by value
    [GpuKernel]
    public static void TwoBoolsByValKernel(LayoutReproTwoBools t, int[] o)
    {
        o[0] = t.F ? 1 : 0;
        o[1] = t.G ? 1 : 0;
        o[2] = t.X;
    }

    // R5b: struct with a char passed by value (#511): the runtime marshaller makes a char one byte by default
    [GpuKernel]
    public static void CharIntByValKernel(LayoutReproCharInt c, int[] o)
    {
        o[0] = c.C;
        o[1] = c.X;
    }

    // R5c: generic struct passed by value (#511)
    [GpuKernel]
    public static void GenericBoxByValKernel(LayoutReproBox<double> b, double[] o)
    {
        o[0] = b.V;
        o[1] = b.Tag;
    }

    // R5d: struct with a Nullable<int> field passed by value (#511)
    [GpuKernel]
    public static void NullableFieldByValKernel(LayoutReproNullableInt n, int[] o)
    {
        o[0] = n.V ?? -1;
        o[1] = n.X;
    }

    // R5e: struct with multiple bool fields passed as an in parameter (#511)
    [GpuKernel]
    public static void TwoBoolsByInKernel(in LayoutReproTwoBools t, int[] o)
    {
        o[0] = t.F ? 1 : 0;
        o[1] = t.G ? 1 : 0;
        o[2] = t.X;
    }

    // R5f: struct with multiple bool fields passed as a ref readonly parameter (#511)
    [GpuKernel]
    public static void TwoBoolsByRefReadOnlyKernel(ref readonly LayoutReproTwoBools t, int[] o)
    {
        o[0] = t.F ? 1 : 0;
        o[1] = t.G ? 1 : 0;
        o[2] = t.X;
    }

    // R5g: each thread changes its own copy of a value parameter
    [GpuKernel]
    public static void MutateByValKernel(LayoutReproTwoBools t, int[] o)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        t.X += tid;
        o[tid] = t.X;
    }

    // R5h: Pack = 1 struct passed by value
    [GpuKernel]
    public static void PackedByValKernel(LayoutReproPacked p, int[] o)
    {
        o[0] = p.B;
        o[1] = p.I;
        o[2] = p.F ? 1 : 0;
    }

    // R5i: struct holding a bool struct and a char passed by value
    [GpuKernel]
    public static void NestedByValKernel(LayoutReproNested n, int[] o)
    {
        o[0] = n.T.F ? 1 : 0;
        o[1] = n.T.X;
        o[2] = n.C;
        o[3] = n.Y;
    }

    // R6a: explicit layout (union) passed by value (#515)
    [GpuKernel]
    public static void UnionKernel(LayoutReproIntFloatUnion u, int[] o)
    {
        o[0] = u.I;
        unsafe
        {
            LayoutReproIntFloatUnion s = default;
            LayoutReproIntFloatUnion* p = &s;
            o[1] = (int)((byte*)(p + 1) - (byte*)p);
        }
    }

    // R6b: array of explicit layout (union) structs (#515)
    [GpuKernel]
    public static void UnionArrayKernel(LayoutReproIntFloatUnion[] u, int[] o)
    {
        o[0] = u[1].I;
    }

    // R7: array of LayoutKind.Auto structs (#515)
    [GpuKernel]
    public static void AutoArrayKernel(LayoutReproAutoLayout[] a, int[] o)
    {
        o[0] = a[0].A;
        o[1] = (int)a[0].B;
        o[2] = a[0].C;
        o[3] = a[1].A;
    }

    // R7b: an [Out] array of LayoutKind.Auto structs whose elements the kernel writes only in part
    [GpuKernel]
    public static void OutAutoArrayKernel([System.Runtime.InteropServices.Out] LayoutReproAutoLayout[] a)
    {
        a[0].A = 7;
    }

    // R9: [InlineArray] whose element contains an array field (S-26)
    [GpuKernel]
    public static void InlineArrFieldKernel(ref LayoutReproArrOuter x, int[] o)
    {
        o[0] = x.P[1].Data[0];
    }
}
