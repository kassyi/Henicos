#nullable enable
namespace IrSequenceCases;

public struct Cell { public int V; }

public static class Forms
{
    public static int Bump(ref int x) { x += 1; return x; }
    public static double Next(ref double v) { v += 1.0; return v; }
    public static int Touch(int[] m) { m[0] += 1; return m[0]; }
    public static long BumpL(ref int x) { x += 1; return x; }
    public static double NextD(double[] d) { d[0] += 1.0; return d[0]; }
    public static double NextF(ref float f) { f += 1.0f; return f; }

    public static void NoInterference(int a, int b, int[] r) { r[0] = a + b; }
    public static void BinaryWriteThenRead(int x, int[] r) { r[0] = Bump(ref x) + x; }
    public static void ParenthesizedBinary(int x, int[] r) { r[0] = (Bump(ref x) + x); }
    public static void BinaryTrapAfterWrite(int[] m, int d, int[] r) { r[0] = Touch(m) + 10 / d; }
    public static void ShiftKeepsTheOrder(int x, uint[] r) { r[0] = (uint)Bump(ref x) << x; }
    public static void FloatRemainder(double v, double[] d) { d[0] = Next(ref v) % v; }
    public static void NestedBinary(int x, int[] r) { r[0] = (Bump(ref x) + x) + Bump(ref x); }
    public static void WhileCondition(int x) { while (Bump(ref x) + x < 10) { x++; } }
    public static void ElementAssignment(int i, int[] r) { r[Bump(ref i)] = i; }
    public static void FieldAssignment(int i, Cell[] cells) { cells[Bump(ref i)].V = i; }
    public static void LocalAssignment(int x) { x = Bump(ref x); }
    public static void CompoundNoCopy(int i, int[] r) { r[Bump(ref i)] += i; }
    public static void CompoundWithCopy(int i, int[] r) { r[i] += Touch(r); }
    public static void CompoundPlain(int i, int a, int[] r) { r[i] += a; }
    public static void TargetPart(int k, Cell[] cells) { cells[k++].V += 3; }
    public static void StepTargetPart(int k, int[] data) { data[k++]++; }
    public static void CheckedElement(int[] m, int[]?[] arrs, int[] r) { r[0] = arrs[0]![Touch(m)]; }
    public static void StackArrayLength(int[] m, int[] r) { int[] s = new int[4]; r[0] = Touch(m) + s.Length; }
    public static void CompoundFloatRemainder(int i, double[] d) { d[i] %= NextD(d); }
    public static void FloatRemainderTargetPart(int k, double[] d) { d[k++] %= 2.0; }
    public static void ElementAssignmentWithInterferingIndex(int[] m, int[]?[] arrs) { arrs[0]![Touch(m)] = m[1]; }
    public static void PromotedBinary(int i, long[] r) { r[0] = i + BumpL(ref i); }
    public static void ConvertedAssignmentValue(int i, long[] r) { r[Bump(ref i)] = i; }
    public static void RemainderWithFloat(float f, double[] d) { d[0] = NextF(ref f) % f; }
    public static void EnumAssignmentValue(int i, Color[] c) { c[Bump(ref i)] = (Color)i; }
}

public enum Color { A, B }
