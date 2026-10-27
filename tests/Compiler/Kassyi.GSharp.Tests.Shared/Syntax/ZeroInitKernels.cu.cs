// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A struct whose default value has every field zero.</summary>
public struct ZeroInitPair
{
    public int X;
    public int Y;
}

/// <summary>A struct with a constructor, for the explicit initializers that <c>[SkipLocalsInit]</c> keeps (#622).</summary>
public struct ZeroInitCtorPair
{
    public int X;
    public int Y;

    public ZeroInitCtorPair(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Eight <c>int</c> values in one struct, the form GSH0001 suggests in place of a <c>List&lt;int&gt;</c>.</summary>
[InlineArray(8)]
public struct ZeroInitItems8
{
    private int _element0;
}

/// <summary>A generic struct whose computed property reads a stackalloc of its type parameter before writing it.</summary>
public struct ZeroInitGenericBox<T> where T : unmanaged
{
    public int Index;

    public T Element
    {
        get
        {
            Span<T> s = stackalloc T[8];
            return s[Index & 7];
        }
    }
}

/// <summary>
/// Stack arrays that C# fills with zeros (#623): a <c>stackalloc</c> without an initializer when no
/// <c>[SkipLocalsInit]</c> applies, and <c>new T[N]</c> with a constant size. Each kernel first fills an array of the
/// same shape in a sibling block, so that a declaration that is not zeroed reads the old values of the reused stack
/// slot instead of zeros.
/// </summary>
public static partial class ZeroInitKernels
{
    // #623 A: the second block reads its stackalloc before writing it
    [GpuKernel]
    public static void StackAllocInSiblingBlocksKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        {
            Span<int> dirty = stackalloc int[4];
            dirty[0] = 7;
            dirty[1] = 7;
            dirty[2] = 7;
            dirty[3] = 7;
            output[tid * 2] = dirty[0] + dirty[1] + dirty[2] + dirty[3] + tid;
        }

        {
            Span<int> clean = stackalloc int[4];
            output[tid * 2 + 1] = clean[0] + clean[1] + clean[2] + clean[3];
        }
    }

    // new T[N] is a zeroed array in C#, whatever [SkipLocalsInit] says, and its Length is N
    [GpuKernel]
    public static void NewArrayKernel(int[] output)
    {
        {
            int[] dirty = new int[10];
            for (int i = 0; i < 10; i++)
            {
                dirty[i] = 9;
            }

            output[2] = dirty[4];
        }

        {
            int[] a = new int[10];
            a[3] = 4;
            output[0] = a[3] + a[4];
            output[1] = a.Length;
        }
    }

    // Each element type reads default(T): output[k] counts the elements of type k that are not the default
    [GpuKernel]
    public static unsafe void ElementTypesKernel(int[] output, int[] sink)
    {
        int marker = 5;
        {
            Span<ZeroInitPair> dirtyPairs = stackalloc ZeroInitPair[4];
            Span<float> dirtyFloats = stackalloc float[4];
            Span<bool> dirtyBools = stackalloc bool[4];
            Span<byte> dirtyBytes = stackalloc byte[4];
            Span<ulong> dirtyLongs = stackalloc ulong[4];
            int*[] dirtyPointers = new int*[4];
            for (int i = 0; i < 4; i++)
            {
                dirtyPairs[i].X = 7;
                dirtyPairs[i].Y = 7;
                dirtyFloats[i] = 1.5f;
                dirtyBools[i] = true;
                dirtyBytes[i] = 0xAB;
                dirtyLongs[i] = 0xFFFF_FFFF_FFFF_FFFFUL;
                dirtyPointers[i] = &marker;
            }

            sink[0] = dirtyPairs[3].X + dirtyPairs[3].Y + (int)dirtyFloats[3] + (dirtyBools[3] ? 1 : 0) + dirtyBytes[3] +
                      (int)(dirtyLongs[3] & 1UL) + *dirtyPointers[3];
        }

        {
            Span<ZeroInitPair> pairs = stackalloc ZeroInitPair[4];
            Span<float> floats = stackalloc float[4];
            Span<bool> bools = stackalloc bool[4];
            Span<byte> bytes = stackalloc byte[4];
            Span<ulong> longs = stackalloc ulong[4];
            int*[] pointers = new int*[4];
            int badPairs = 0;
            int badFloats = 0;
            int badBools = 0;
            int badBytes = 0;
            int badLongs = 0;
            int badPointers = 0;
            for (int i = 0; i < 4; i++)
            {
                if (pairs[i].X != 0 || pairs[i].Y != 0)
                {
                    badPairs++;
                }

                if (floats[i] != 0f)
                {
                    badFloats++;
                }

                if (bools[i])
                {
                    badBools++;
                }

                if (bytes[i] != 0)
                {
                    badBytes++;
                }

                if (longs[i] != 0UL)
                {
                    badLongs++;
                }

                if (pointers[i] != null)
                {
                    badPointers++;
                }
            }

            output[0] = badPairs;
            output[1] = badFloats;
            output[2] = badBools;
            output[3] = badBytes;
            output[4] = badLongs;
            output[5] = badPointers;
        }
    }

    // A stackalloc in a loop body is a new zeroed block on every iteration (C# allocates it each time)
    [GpuKernel]
    public static void StackAllocInLoopKernel(int[] output)
    {
        int acc = 0;
        for (int i = 0; i < 3; i++)
        {
            Span<int> s = stackalloc int[4];
            acc += s[0] + s[1] + s[2] + s[3];
            s[0] = i + 1;
            s[1] = i + 1;
            s[2] = i + 1;
            s[3] = i + 1;
            output[1 + i] = s[0] + s[3];
        }

        output[0] = acc;
    }

    // Fills a frame of the same size as the readers below; a reader called next at the same depth that is not zeroed
    // sees these values, whether or not the compiler flattens blocks or keeps the arrays in registers
    [GpuDeviceFunction]
    [GpuNoInline]
    public static int DirtyFrame(int value, int index)
    {
        Span<int> junk = stackalloc int[8];
        for (int i = 0; i < 8; i++)
        {
            junk[(i + index) & 7] = value;
        }

        return junk[index & 7] + junk[(index + 5) & 7];
    }

    // A stackalloc of a type parameter (in a generic struct; G# has no generic device functions) is zeroed in every
    // instantiation, and a Span over new T[N] is a zeroed stack array
    [GpuKernel]
    public static void GenericStackAllocKernel(int[] output, float[] floats, int index)
    {
        var intBox = new ZeroInitGenericBox<int> { Index = index };
        var floatBox = new ZeroInitGenericBox<float> { Index = index };
        output[2] = DirtyFrame(7, index);
        output[0] = intBox.Element;
        output[3] = DirtyFrame(9, index);
        floats[0] = floatBox.Element;
        Span<int> fromArray = new int[4];
        output[1] = fromArray[index & 3] + fromArray.Length;
    }

    // A ReadOnlySpan over a stackalloc is a const array, which C++ only accepts with an initializer
    [GpuKernel]
    public static void ReadOnlyStackAllocKernel(int[] output)
    {
        ReadOnlySpan<int> r = stackalloc int[4];
        output[0] = r[0] + r[1] + r[2] + r[3];
        output[1] = r.Length;
    }

    // [SkipLocalsInit] does not leave out the initializer of a const array, which C++ requires
    [GpuKernel]
    [SkipLocalsInit]
    public static void ReadOnlyStackAllocSkipLocalsInitKernel(int[] output)
    {
        ReadOnlySpan<int> r = stackalloc int[4];
        output[0] = r[0] + r[1] + r[2] + r[3];
        output[1] = r.Length;
    }

    [GpuDeviceFunction]
    public static int SumSpan(Span<int> values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    [GpuDeviceFunction]
    public static int SumArray(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    [GpuDeviceFunction]
    public static int Length(ReadOnlySpan<int> values) => values.Length;

    // A stack array passes as a span or an array argument with its length
    [GpuKernel]
    public static void StackArrayArgumentKernel(int[] output)
    {
        int[] a = new int[10];
        a[3] = 4;
        Span<int> s = stackalloc int[4];
        s[1] = 2;
        output[0] = SumSpan(a);
        output[1] = SumArray(a);
        output[2] = Length(s);
        ReadOnlySpan<int> r = s;
        output[3] = Length(r);
        output[4] = SumSpan(s);
        output[5] = Length(a);
    }

    // A local array is never null in C#
    [GpuKernel]
    public static void StackArrayNullTestKernel(int[] output)
    {
        int[] a = new int[4];
        output[0] = a == null ? 1 : 0;
        output[1] = a != null ? 1 : 0;
        output[2] = a is null ? 1 : 0;
        output[3] = a is not null ? 1 : 0;
        output[4] = null == a ? 1 : 0;
        output[5] = (int[])a == null ? 1 : 0;
    }

    // A null test through a cast keeps the cast as its operand, also on an array the kernel receives
    [GpuKernel]
    public static void CastNullTestKernel(int[] input, int[] output)
    {
        output[0] = (int[])input == null ? 1 : 0;
        output[1] = (int[])input != null ? 1 : 0;
    }

    // A stack array as the receiver of span members and of StackLinq
    [GpuKernel]
    public static void StackArrayMembersKernel(int[] output)
    {
        Span<int> s = stackalloc int[4];
        s.Fill(2);
        output[0] = s[3];
        Span<int> tail = s.Slice(1);
        output[1] = tail.Length;
        int[] a = { 1, 2, 3, 4 };
        Span<int> view = a.AsSpan();
        output[2] = view[2];
        output[3] = a.AsStackEnumerable().Sum();
        output[4] = s.AsStackEnumerable().Sum();
    }

    [GpuDeviceFunction]
    public static void KeepIn(int[] x, ref int[] slot) => slot = x;

    // A local and a ref argument that hold a stack array within its scope see the array of the same iteration
    [GpuKernel]
    public static void StackArrayAliasKernel(int[] output, int[] seed)
    {
        for (int it = 0; it < 2; it++)
        {
            int[] a = { 11 * (it + 1), 0, 0, 0 };
            int[] b = a;
            int[] inner = seed;
            KeepIn(a, ref inner);
            output[it] = b[0] + inner[0];
        }
    }

    // [SkipLocalsInit] leaves out only the zero fill of a stackalloc: every explicit initializer keeps its C# value (#622)
    [GpuKernel]
    [SkipLocalsInit]
    public static void SkipLocalsInitInitializersKernel(int[] output)
    {
        {
            ZeroInitCtorPair dirtyA = new(9, 9);
            ZeroInitCtorPair dirtyB = new(9, 9);
            ZeroInitCtorPair dirtyC = new(9, 9);
            ZeroInitCtorPair dirtyD = new(9, 9);
            ZeroInitCtorPair dirtyE = new(9, 9);
            output[10] = dirtyA.X + dirtyB.Y + dirtyC.X + dirtyD.Y + dirtyE.X;
        }

        {
            ZeroInitCtorPair a = default;
            ZeroInitCtorPair b = default(ZeroInitCtorPair);
            ZeroInitCtorPair c = new ZeroInitCtorPair();
            ZeroInitCtorPair d = new(1, 2);
            ZeroInitCtorPair e = new ZeroInitCtorPair() { X = 5 };
            output[0] = a.X;
            output[1] = a.Y;
            output[2] = b.X;
            output[3] = b.Y;
            output[4] = c.X;
            output[5] = c.Y;
            output[6] = d.X;
            output[7] = d.Y;
            output[8] = e.X;
            output[9] = e.Y;
        }
    }

    // Fills the frame that ReadDefaults reuses when it is called next at the same depth, so a reader whose = default
    // is left out reads these values (a sibling block in the kernel would not do: the emitted C++ flattens blocks)
    [GpuDeviceFunction]
    [GpuNoInline]
    public static int DirtyDefaultsFrame(int value, int index)
    {
        ZeroInitPair pair = new ZeroInitPair { X = value, Y = value };
        ZeroInitItems8 items = default;
        for (int i = 0; i < 8; i++)
        {
            items[(i + index) & 7] = value;
        }

        return pair.X + pair.Y + items[index & 7] + items[(index + 4) & 7];
    }

    // = default under [SkipLocalsInit] still zeroes a struct and an [InlineArray] struct, as C# does
    [GpuDeviceFunction]
    [GpuNoInline]
    [SkipLocalsInit]
    public static int ReadDefaults(int index)
    {
        ZeroInitPair r = default;
        ZeroInitItems8 items = default;
        return r.X + r.Y + items[index & 7] + items[(index + 4) & 7];
    }

    [GpuKernel]
    public static void SkipLocalsInitDefaultKernel(int[] output, int index)
    {
        output[1] = DirtyDefaultsFrame(7, index);
        output[0] = ReadDefaults(index);
    }
}
