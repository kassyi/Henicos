using Kassyi.GSharp.Core;

namespace IrDecisionCases;

public enum Narrow : byte { A = 1, B = 2 }

public static class Numeric
{
    public const byte ByteMax = 255;

    public static void FoldedShift(int[] r) { r[0] = 3 << 33; }
    public static void DivideByAny(int a, int b, int[] r) { r[0] = a / b; }
    public static void DivideByPositive(int a, int[] r) { r[0] = a / BlockDimension.X; }
    public static void RemainderFloat(float x, float y, float[] r) { r[0] = x % y; }
    public static void ShiftUInt(uint u, int n, uint[] r) { r[0] = u << n; }
    public static void MixedUnsigned(uint u, int i, long[] r) { r[0] = u + i; }
    public static void NegateUnsigned(uint u, long[] r) { r[0] = -u; }
    public static void NarrowEnumOr(Narrow a, Narrow b, Narrow[] r) { r[0] = a | b; }
    public static void IncrementShort(short s, short[] r) { r[0] = ++s; }
    public static void CompoundDivide(int x, int y, int[] r) { x /= y; r[0] = x; }
    public static void CompoundShiftULong(ulong x, int n, ulong[] r) { x <<= n; r[0] = x; }
    public static void FloatToInt(float f, int[] r) { r[0] = (int)f; }
    public static void FloatToByte(float f, byte[] r) { r[0] = (byte)f; }
    public static void FoldedCast(int[] r) { r[0] = (int)(3L << 65); }
    public static void ConstNoConversion(byte[] r) { r[0] = ByteMax; }
    public static void ConstUnderConversion(int[] r) { r[0] = Take(ByteMax); }
    public static void NegatedFloatLiteral(float[] r) { r[0] = -1.5f; }
    public static void NegatedIntMin(int[] r) { r[0] = -2147483648; }
    public static void ConditionalMixed(bool c, uint u, int i, long[] r) { r[0] = c ? u : i; }
    public static void ConversionToObject(int i, object[] r) { r[0] = i; }

    public static void ShuffleOperandByte(byte[] data, int i, uint m, int v, int[] r)
    {
        r[0] = data[i] + (byte)Warp.ShuffleSync(m, v, 1);
    }

    public static void ShuffleOperandPromote(uint u, int i, uint m, int v, long[] r)
    {
        r[0] = u + i + Warp.ShuffleSync(m, v, 1);
    }

    private static int Take(int value) => value;
}
