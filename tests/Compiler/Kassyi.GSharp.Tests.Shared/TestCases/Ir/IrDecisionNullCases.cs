#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace IrDecisionNullCases;

public struct Holder
{
    public int[] Data;
}

public static class Nulls
{
    public static void ReadParameter(int[] a, int i, int[] r) { r[0] = a[i]; }
    public static void ReadNotNull(int[] a, int i, int[] r) { int[] b = Gpu.NotNull(a); r[0] = b[i]; }
    public static void ReadNotNullAcrossBarrier(int[] a, int i, int[] r)
    {
        int[] b = Gpu.NotNull(a);
        Gpu.SyncThreads();
        r[0] = b[i];
    }
    public static void ReadWrittenLocal(int[] a, int[] c, int i, int[] r)
    {
        int[] b = Gpu.NotNull(a);
        b = c;
        r[0] = b[i];
    }
    public static void ReadStackArray(int i, int[] r) { int[] s = new int[4]; r[0] = s[i]; }
    public static void ReadConditional(bool c, int[] a, int[] b, int i, int[] r) { r[0] = (c ? a : b)[i]; }
    public static void ReadULongIndex(int[] a, ulong i, int[] r) { r[0] = a[i]; }
    public static void StoreElement(int[] a, int i, int v) { a[i] = v; }
    public static void StoreStackArray(int i, int v) { int[] s = new int[4]; s[i] = v; }
    public static void LengthOfParameter(int[] a, int[] r) { r[0] = a.Length; }
    public static void LengthOfStackArray(int[] r) { int[] s = new int[4]; r[0] = s.Length; }
    public static void NullTestArray(int[] a, bool[] r) { r[0] = a == null; }
    public static void NullTestStackArray(bool[] r) { int[] s = new int[4]; r[0] = s != null; }
    public static void NullTestCast(int[] a, bool[] r) { r[0] = (int[])a == null; }
    public static void CoalesceArray(int[]? a, int[] b, int[][] r) { r[0] = a ?? b; }
    public static void CoalesceNullable(int? a, long b, long[] r) { r[0] = a ?? b; }
    public static void CallPlain(int[] r) { r[0] = Plain(1); }
    public static void CallPlainWithByte(byte b, int[] r) { r[0] = Plain(b); }
    public static void CallIntrinsic(int[] r) { r[0] = ThreadIndex.X; }
    public static void CallMappedWithInt(int i, float[] r) { r[0] = Math.Max(i, 1.5f); }
    public static void ReadInitializerWithSplit(bool c, int[] a, int[] a2, uint m, int v, int i, int[] r)
    {
        int[] b = c ? Gpu.NotNull(Pick(a, Warp.ShuffleSync(m, v, 1))) : Gpu.NotNull(a2);
        r[0] = b[i];
    }
    public static void ReadInitializerConditional(bool c, int[] a, int[] a2, int i, int[] r)
    {
        int[] b = c ? Gpu.NotNull(a) : a2;
        r[0] = b[i];
    }
    public static void AbsOfInt(int x, int[] r) { r[0] = Math.Abs(x); }
    public static void NullTestString(string? s, bool[] r) { r[0] = s == null; }
    public static void ReadStackArrayAcrossBarrier(int i, int[] r)
    {
        int[] s = new int[4];
        Gpu.SyncThreads();
        r[0] = s[i];
    }
    public static void BclPopCountUInt(uint u, int[] r) { r[0] = BitOperations.PopCount(u); }
    public static void BclPopCountByte(byte b, int[] r) { r[0] = BitOperations.PopCount(b); }
    public static void NotNullArrayThenShuffle(int[] a, int i, uint m, int v)
    {
        Gpu.NotNull(a)[i] += Warp.ShuffleSync(m, v, 1);
    }
    public static void ReadNullForgiven(int[]? a, int i, int[] r) { r[0] = a![i]; }
    public static void ReadParenthesizedCoalesce(int[]? pick, int[] b, int i, int[] r)
    {
        r[0] = (pick ?? b)[i];
    }
    public static void ReadNUIntIndex(int[] a, nuint i, int[] r) { r[0] = a[i]; }
    public static void ReadNIntIndex(int[] a, nint i, int[] r) { r[0] = a[i]; }
    public static void LengthOfParenthesizedNullForgiven(int[]? a, int[] r) { r[0] = (a!).Length; }

    private static int[] Pick(int[] a, int unused) => a;

    private static int Plain(int value) => value;
}
