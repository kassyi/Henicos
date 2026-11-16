using Kassyi.GSharp.Core;

namespace IrNumericCases;

public enum Small : byte { A = 1, B = 2 }

public static class Numeric
{
    public static void AddInts(int a, int b) { var result = a + b; }
    public static void AddThreadIndices() { var result = ThreadIndex.X + ThreadIndex.Y; }
    public static void DivideByLength(int a, int[] data) { var result = a / data.Length; }
    public static void DivideByCount(int a, byte b) { var result = a / b; }
    public static void ShiftRightInt(int a, int n) { var result = a >> n; }
    public static void MixedUnsigned(uint u, int i) { var result = u + i; }
    public static void NegateUnsigned(uint u) { var result = -u; }
    public static void NegateByte(byte b) { var result = -b; }
    public static void SmallOr(Small a, Small b) { var result = a | b; }
    public static void ShortCompound(short s, int i) { s += i; var result = s; }
    public static void ULongShiftCompound(ulong x, int n) { x <<= n; var result = x; }
    public static void IncrementShort(short s) { var result = ++s; }
    public static void IncrementUInt(uint u) { var result = u++; }
}
