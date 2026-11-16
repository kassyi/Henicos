#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace IrTrapCases;

public static class Traps
{
    public static void DivideByAny(int a, int b) { var result = a / b; }
    public static void DivideByPositive(int a) { var result = a / BlockDimension.X; }
    public static void CheckedAdd(int a, int b) { var result = checked(a + b); }
    public static void ElementRead(int[] data, int i) { var result = data[i]; }
    public static void LengthRead(int[] data) { var result = data.Length; }
    public static void UnwrapNullable(int? n) { var result = (int)n; }
    public static void FloatMath(float x) { var result = MathF.Sqrt(x); }
    public static void IsNullTest(int[] data) { var result = data is null; }
    public static void ElementStore(int[] data, int i, int v) { data[i] = v; }
    public static void ElementStoreAtULong(int[] data, ulong i, int v) { data[i] = v; }

    public static void TakesArray(int[] data) { }
    public static void TakesNullableArray(int[]? data) { }
    public static void PassesNew() { int[] a = new int[4]; TakesArray(a); }
}
