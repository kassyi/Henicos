#nullable enable
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace IrSequenceCases;

[InlineArray(2)]
public struct InlineCellBuf
{
    private Cell _e0;
}

public partial struct Cell
{
    public int V;

    [GpuDeviceFunction]
    public int Add(int x)
    {
        V += x;
        return V;
    }

    [GpuDeviceFunction]
    public readonly int Read() => V;
}

public ref partial struct RefCell
{
    public int V;

    [GpuDeviceFunction]
    public int Add(int x)
    {
        V += x;
        return V;
    }
}

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
    public static void NullPatternOperand(int[]? p, int[] r) { r[0] = (r[1] = 1) + (p is null ? 1 : 0); }
    public static void NotNullPatternOperand(int[]? p, int[] r) { r[0] = (r[1] = 1) + (p is not null ? 1 : 0); }
    public static void ImplicitWidening(int i, long[] r) { r[0] = i + (r[1] = 5L); }
    public static void NestedWidening(bool c, int i, long l, long[] r) { r[0] = (r[1] = 5L) + (c ? i : l); }
    public static void LengthOfANotNullLocal(int[] a, int[] m, int[] r)
    {
        int[] b = Gpu.NotNull(a);
        r[0] = Touch(m) + b.Length;
    }

    [GpuDeviceFunction]
    public static int Two(int a, int b) => a + b;
    [GpuDeviceFunction]
    public static int Three(int a, int b, int c) => a + b + c;
    [GpuDeviceFunction]
    public static int Pair(int a, ref int b) { b += a; return a; }
    [GpuDeviceFunction]
    public static int RefFirst(ref int b, int a, int c) { b += a; return c; }
    [GpuDeviceFunction]
    public static int WithDefault(int a, int b = 5) => a + b;
    [GpuDeviceFunction]
    public static int Use(int[] s, int v) => v;
    [GpuDeviceFunction]
    public static int ReadIn(int a, in int b) => a + b;
    [GpuDeviceFunction]
    public static long Widen(long a, long b) => a + b;
    public static void CallNoInterference(int a, int b, int[] r) { r[0] = Two(a, b); }
    public static void CallArgumentsInterfere(int x, int[] r) { r[0] = Two(Bump(ref x), x); }
    public static void CallNamedNoInterference(int a, int b, int[] r) { r[0] = Two(b: a, a: b); }
    public static void CallNamedWithInterference(int x, int[] r) { r[0] = Two(b: Bump(ref x), a: x); }
    public static void CallThree(int x, int[] r) { r[0] = Three(Bump(ref x), x, 1); }
    public static void CallRefArgument(int x, int y, int[] r) { r[0] = Pair(Bump(ref x), ref y); }
    public static void CallRefArgumentFirst(int i, int[] r) { r[1] = RefFirst(ref r[i], Bump(ref i), i); }
    public static void NullForgivingElement(int[]? a, int[] m, int[] r) { r[0] = (a!)[Touch(m)]; }
    public static void CallThreeAfterLast(int x, int y, int[] r) { r[0] = Three(Bump(ref x), x, y); }
    public static void CreationWithDefault(int a, int b, Cell3[] r) { r[0] = new Cell3(b: a, a: b); }
    public static void CallSingleArgument(int x, int[] r) { r[0] = Bump(ref x); }
    public static void CallWithDefault(int x, int[] r) { r[0] = WithDefault(Bump(ref x)); }
    public static void CallProven(int[] m, int v, int[] r) { int[] s = new int[4]; r[0] = Use(s, Touch(m)); }
    public static void CallUnproven(int[] m, int[] a, int[] r) { r[0] = Use(a, Touch(m)); }
    public static void CallInArgument(int x, int y, int[] r) { r[0] = ReadIn(Bump(ref x), in y); }
    public static void CallConvertedArguments(int x, long[] r) { r[0] = Widen(Bump(ref x), x); }
    public static void CallNestedInArgument(int x, int[] r) { r[0] = Two(Two(Bump(ref x), x), x); }
    public static void CallMappedInterfere(int x, int[] r) { r[0] = System.Math.Max(Bump(ref x), x); }
    public static void CallMappedNamed(int a, int b, int[] r) { r[0] = System.Math.Max(val2: a, val1: b); }
    public static void CreationOutOfOrder(int a, int b, Cell2[] r) { r[0] = new Cell2(b: a, a: b); }
    public static void CreationInOrder(int a, int b, Cell2[] r) { r[0] = new Cell2(a, b); }
    public static void CreationInterfere(int x, Cell2[] r) { r[0] = new Cell2(Bump(ref x), x); }
    [GpuDeviceFunction]
    public static int AddTo(this ref int a, int b) { a += b; return a; }
    [GpuDeviceFunction]
    public static int Plus(this in int a, int b) => a + b;
    [GpuDeviceFunction]
    public static int ExtAdd(this ref Cell c, int x) { c.V += x; return c.V; }
    public static void ExtensionRefReceiver(int i, int[] r) { r[0] = r[i].AddTo(Bump(ref i)); }
    public static void ExtensionInReceiver(int i, int[] r) { r[0] = r[i].Plus(Bump(ref i)); }
    public static void ParenthesizedInstanceCallOrder(Cell c, int[] r) { r[0] = c.V + ((c.Add))(1); }
    public static void ParenthesizedInstanceCallOrderFirst(Cell c, int[] r) { r[0] = ((c.Add))(1) + c.V; }
    public static void ParenthesizedExtensionCallOrder(Cell c, int[] r) { r[0] = c.V + ((c.ExtAdd))(1); }

    [GpuDeviceFunction]
    public static Cell MakeCell(int v) => new() { V = v };

    [GpuDeviceFunction]
    public static int BumpCell(ref Cell c)
    {
        c.V += 1;
        return c.V;
    }

    [GpuDeviceFunction]
    public static int BumpRefCell(ref RefCell c)
    {
        c.V += 1;
        return c.V;
    }

    [GpuDeviceFunction]
    public static int BumpBool(ref bool c)
    {
        c = !c;
        return 1;
    }

    public static void InstanceRefReceiver(int i, Cell[] cells, int[] r) { r[0] = cells[i].Add(Bump(ref i)); }
    public static void InstanceInReceiverCopy(in Cell p, ref Cell q, int[] r) { r[0] = p.Add(BumpCell(ref q)); }
    public static void InstanceRefReadOnlyLocalCopy(ref Cell q, int[] r)
    {
        ref readonly var p = ref q;
        r[0] = p.Add(BumpCell(ref q));
    }
    public static void InstanceRefStructReceiver(bool c, RefCell a, RefCell b, int[] r)
    {
        r[0] = (c ? ref a : ref b).Add(BumpBool(ref c));
    }
    public static void InstanceRefStructLocalNoSpill(RefCell rc, int[] r) { r[0] = rc.Add(BumpRefCell(ref rc)); }
    public static void InstanceRvalueReceiver(int x, int[] r) { r[0] = MakeCell(x).Add(Bump(ref x)); }
    public static void InstanceRefReadOnlyConditionalCopy(bool c, in Cell a, in Cell b, int[] r)
    {
        r[0] = (c ? ref a : ref b).Add(BumpBool(ref c));
    }

    public static void InstanceMixedRefReadOnlyConditionalCopy(bool c, ref Cell a, in Cell b, int[] r)
    {
        r[0] = (c ? ref a : ref b).Add(BumpBool(ref c));
    }

    public static void InstanceConstFalseRefConditionalAddress(ref Cell a, Cell[] cells, int i, int[] r)
    {
        ref readonly var ro = ref a;
        r[0] = (false ? ref ro : ref cells[i]).Add(Bump(ref i));
    }

    public static void InstanceConstTrueRefConditionalReadOnlyCopy(ref Cell a, ref Cell b, int[] r)
    {
        ref readonly var ro = ref a;
        r[0] = (true ? ref ro : ref b).Add(BumpCell(ref b));
    }
}

public static class InlineArrayForms
{
    public static int InstanceInInlineArrayElementCopy(in InlineCellBuf buf, ref Cell q) =>
        buf[0].Add(Forms.BumpCell(ref q));
}

public partial struct CellIn
{
    public int A;
    public int B;

    public CellIn(in int a, int b)
    {
        A = a;
        B = b;
    }
}

// An unconverted rvalue passed to an in parameter: the syntax path binds it by reference (auto&&), the IR copies it
// into a value temporary; both read it before the later argument writes x
public static class InRvalueForms
{
    public static int Bump(ref int x) { x += 1; return x; }
    [GpuDeviceFunction]
    public static int ReadFirst(in int a, int b) => a + b;
    [GpuDeviceFunction]
    public static int PlusIn(this in int a, int b) => a + b;
    public static void CallInRvalue(int x, int[] r) { r[0] = ReadFirst(x + 0, Bump(ref x)); }
    public static void ExtensionInRvalue(int x, int[] r) { r[0] = (x + 0).PlusIn(Bump(ref x)); }
    public static void CreationInRvalue(int x, CellIn[] r) { r[0] = new CellIn(x + 0, Bump(ref x)); }
}

public partial struct Cell3
{
    public int A;
    public int B;
    public int C;

    public Cell3(int a, int b, int c = 7)
    {
        A = a;
        B = b;
        C = c;
    }
}

public partial struct Cell2
{
    public int A;
    public int B;

    public Cell2(int a, int b)
    {
        A = a;
        B = b;
    }
}

public enum Color { A, B }

public static class SkippedForms
{
    public static int Bump(ref int x) { x += 1; return x; }
    public static int Sum(params int[] values) => values.Length;
    public static void CallParams(int x, int[] r) { r[0] = Sum(Bump(ref x), x); }
}
