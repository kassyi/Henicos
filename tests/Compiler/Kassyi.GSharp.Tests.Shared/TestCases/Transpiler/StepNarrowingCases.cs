// ReSharper disable All
namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler;

public enum ByteEnum : byte { A = 0, B = 1 }
public enum SByteEnum : sbyte { A = 0, B = 1 }
public enum ShortEnum : short { A = 0, B = 1 }
public enum UShortEnum : ushort { A = 0, B = 1 }
public enum IntEnum : int { A = 0, B = 1 }

public static class StepNarrowingCases
{
    public static void SByteStep(sbyte x) { x++; }
    public static void ByteStep(byte x) { x++; }
    public static void ShortStep(short x) { x++; }
    public static void UShortStep(ushort x) { x++; }
    public static void CharStep(char x) { x++; }
    public static void ByteEnumStep(ByteEnum x) { x++; }
    public static void SByteEnumStep(SByteEnum x) { x++; }
    public static void ShortEnumStep(ShortEnum x) { x++; }
    public static void UShortEnumStep(UShortEnum x) { x++; }

    public static void IntStep(int x) { x++; }
    public static void UIntStep(uint x) { x++; }
    public static void LongStep(long x) { x++; }
    public static void ULongStep(ulong x) { x++; }
    public static void FloatStep(float x) { x++; }
    public static void DoubleStep(double x) { x++; }
    public static void DecimalStep(decimal x) { x++; }
    public static void NIntStep(nint x) { x++; }
    public static void NUIntStep(nuint x) { x++; }
    public static void IntEnumStep(IntEnum x) { x++; }
}
