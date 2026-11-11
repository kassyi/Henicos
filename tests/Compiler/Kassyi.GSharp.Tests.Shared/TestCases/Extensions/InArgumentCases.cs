// Each method of Shapes passes its case as the first argument of one call of In or Add; OperationExtensionsTests
// classifies that argument (IsVariable, SpilledArguments.ClassifyByReference)
using System;
using System.Runtime.CompilerServices;

namespace InArgumentCases;

[InlineArray(4)]
public struct Inline4
{
    private int _element;
}

public unsafe struct Fixed4
{
    public fixed int Buf[4];
}

public struct Cell
{
    public int F;

    public int[] Values;

    public ref int Ref => ref Values[0];
}

public static unsafe class Shapes
{
    private static Inline4 s_inline;

    public static int In(in int x) => x;

    public static void Add(ref int x) => x++;

    public static Inline4 MakeInline() => default;

    public static ref Inline4 PickInline() => ref s_inline;

    public static ref int Select(int[] values) => ref values[0];

    public static int[] GetArray() => new int[1];

    // --- Variables kept as a location ---

    public static int Local(int v) => In(v);

    public static int ParenthesizedLocal(int v) => In((v));

    public static int ArrayElement(int[] a) => In(a[0]);

    public static int ArrayOfACall() => In(GetArray()[0]);

    public static int InlineElement(Inline4 b) => In(b[1]);

    public static int ParenthesizedInlineElement(Inline4 b) => In((b[1]));

    public static int FixedElement(Fixed4 f) => In(f.Buf[1]);

    public static int PointerIndirection(int* p) => In(*p);

    public static int PointerElement(int* p) => In(p[1]);

    public static int PointerMember(Cell* c) => In(c->F);

    public static int DereferencedMember(Cell* c) => In((*c).F);

    public static void KeywordPointerIndirection(int* p) => Add(ref *p);

    // --- Variables that a call, a property or a ref conditional selects ---

    public static int RefConditional(bool c, int v, int w) => In(c ? ref v : ref w);

    public static int ParenthesizedRefConditional(bool c, int v, int w) => In((c ? ref v : ref w));

    public static int RefReturn(int[] a) => In(Select(a));

    public static int ElementOfARefReturn() => In(PickInline()[0]);

    public static int RefProperty(Cell c) => In(c.Ref);

    // --- Values, which C# copies ---

    public static int ElementOfAValue() => In(MakeInline()[0]);

    public static int Sum(int v) => In(v + 1);

    public static int SpanElement(Span<int> s) => In(s[0]);
}
