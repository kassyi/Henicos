using System;
using Kassyi.GSharp.Core;

namespace IrCases;

public enum Color : byte { Red, Green }

public struct Cell
{
    public int F;
    public int G;
    public int[] Data;

    public int Peek() => F;

    public readonly int ReadOnlyPeek() => F;

    public void Bump() => F++;

    public int Auto { get; set; }

    public readonly int Computed => F + G;
}

public readonly struct Frozen
{
    public readonly int V;

    public int Read() => V;
}

public struct Outer
{
    public Cell Inner;
}

public struct Holder
{
    public int[] Items;

    public ref int Current => ref Items[0];
}

[System.Runtime.CompilerServices.InlineArray(4)]
public struct Buffer4
{
    private int _element;
}

public class Box
{
    public int Value;
}

public class Node
{
    public static bool operator ==(Node? a, Node? b) => false;

    public static bool operator !=(Node? a, Node? b) => true;

    public override bool Equals(object? obj) => false;

    public override int GetHashCode() => 0;
}

public struct Money
{
    public int Cents;

    public static Money operator +(Money a, Money b) => new Money { Cents = a.Cents + b.Cents };

    public static bool operator ==(Money a, Money b) => a.Cents == b.Cents;

    public static bool operator !=(Money a, Money b) => a.Cents != b.Cents;

    public static implicit operator int(Money m) => m.Cents;

    public override bool Equals(object? obj) => false;

    public override int GetHashCode() => 0;
}

public static class Extensions
{
    [GpuDeviceFunction]
    public static int Twice(this int x) => x * 2;
}

// Each case method ends with 'var result = <expression>;' or holds the statement the test looks at
public static class Ir
{
    public const int K = 3;

    public static int s_counter;

    [GpuDeviceFunction]
    public static int TakeIn(in int value) => value;

    [GpuDeviceFunction]
    public static int TakeRefReadOnly(ref readonly int value) => value;

    [GpuDeviceFunction]
    public static int TakeRef(ref int value) => value;

    [GpuDeviceFunction]
    public static void TakeOut(out int value) => value = 1;

    [GpuDeviceFunction]
    public static int Sum(params int[] values) => values.Length;

    [GpuDeviceFunction]
    public static int Offset(int a, int b = 4, int c = 5) => a + b + c;

    [GpuDeviceFunction]
    public static Cell MakeCell(int j) => new Cell { F = j };

    [GpuDeviceFunction]
    public static ref int Pick(int[] data) => ref data[0];

    [GpuDeviceFunction]
    public static ref readonly int PickReadOnly(int[] data) => ref data[0];

    [GpuDeviceFunction]
    public static ref int Choose(ref int a, ref int b, bool first)
    {
        if (first)
        {
            return ref a;
        }

        return ref b;
    }

    [GpuDeviceFunction]
    public static unsafe int ReadPointer(int* p) => 0;

    // --- Categories ---

    public static void InVariable(int x)
    {
        var result = TakeIn(x);
    }

    public static void InNonVariable(int x)
    {
        var result = TakeIn(x + 1);
    }

    public static void InExplicit(int x)
    {
        var result = TakeIn(in x);
    }

    public static void RefReadOnlyArgument(int x)
    {
        var result = TakeRefReadOnly(in x);
    }

    public static void RefArgument(int x)
    {
        var result = TakeRef(ref x);
    }

    public static void OutVar()
    {
        TakeOut(out var declared);
        var result = declared;
    }

    public static void OutDiscard()
    {
        TakeOut(out _);
    }

    public static void RefLocal(int[] data)
    {
        ref int r = ref data[0];
        r = 5;
    }

    public static void RefReadOnlyLocal(int[] data)
    {
        ref readonly int r = ref data[0];
        var result = r;
    }

    public static ref int ReturnRef(int[] data) => ref data[1];

    public static ref readonly int ReturnRefReadOnly(int[] data) => ref data[1];

    public static void RefConditional(int[] a, int[] b, bool c)
    {
        ref int r = ref c ? ref a[0] : ref b[0];
    }

    public static void ValueConditional(int[] a, int[] b, bool c)
    {
        var result = c ? a[0] : b[0];
    }

    public static void StructMethodOnVariable(Cell cell)
    {
        var result = cell.Peek();
    }

    public static void ReadOnlyMethodOnVariable(Cell cell)
    {
        var result = cell.ReadOnlyPeek();
    }

    public static void ReadOnlyStructMethod(Frozen frozen)
    {
        var result = frozen.Read();
    }

    public static void StructMethodOnValue(int j)
    {
        var result = MakeCell(j).Peek();
    }

    public static void FieldOfValue(int j)
    {
        var result = TakeIn(MakeCell(j).F);
    }

    public static void FieldOfVariable(Cell cell)
    {
        var result = TakeIn(cell.F);
    }

    public static void NestedFieldWrite(Outer outer)
    {
        outer.Inner.F = 1;
    }

    public static void ReferenceField(Box box)
    {
        var result = TakeIn(box.Value);
    }

    public static void StaticField()
    {
        var result = TakeIn(s_counter);
    }

    public static void RefReturnProperty(Holder holder)
    {
        var result = TakeRef(ref holder.Current);
    }

    public static void RefReturnCall(int[] data)
    {
        var result = TakeRef(ref Pick(data));
    }

    public static void RefReturnCallRead(int[] data)
    {
        var result = Pick(data);
    }

    public static void ArrayElementIn(int[] data, int i)
    {
        var result = TakeIn(data[i]);
    }

    public static void InlineArrayElementIn(Buffer4 buf)
    {
        var result = TakeIn(buf[0]);
    }

    public static void RefConditionalIn(bool c, int[] a, int[] b)
    {
        var result = TakeIn(c ? ref a[0] : ref b[0]);
    }

    public static unsafe void PointerIndirectionIn(int* p)
    {
        var result = TakeIn(*p);
    }

    public static void PropertyWrite(Cell cell)
    {
        cell.Auto = 3;
    }

    public static void ComputedRead(Cell cell)
    {
        var result = cell.Computed;
    }

    public static void SpanIndexer(Span<int> span, int i)
    {
        var result = span[i];
    }

    public static unsafe void AddressOf(int x)
    {
        var result = ReadPointer(&x);
    }

    public static unsafe void PointerRead(int* p)
    {
        var result = ReadPointer(p);
    }

    public static void ThisField(Cell cell)
    {
        cell.Bump();
    }

    // --- Arguments ---

    public static void Defaults(int a)
    {
        var result = Offset(a);
    }

    public static void NamedOutOfOrder(int a)
    {
        var result = Offset(c: 1, a: a);
    }

    public static void Params(int a, int b)
    {
        var result = Sum(a, b);
    }

    public static void ParamsEmpty()
    {
        var result = Sum();
    }

    public static void Extension(int a)
    {
        var result = a.Twice();
    }

    // --- Expressions ---

    public static void Literal()
    {
        var result = 7;
    }

    public static void ConstField()
    {
        var result = K;
    }

    public static void ConstLocal()
    {
        const int Local = 9;
        var result = Local;
    }

    public static void ConstantExpression()
    {
        var result = 1 + 2;
    }

    public static void Logical(bool a, bool b)
    {
        var result = a && !b;
    }

    public static void NullEquals(int[] data)
    {
        var result = data == null;
    }

    public static void NullNotEquals(int[] data)
    {
        var result = null != data;
    }

    public static void IsNull(int[] data)
    {
        var result = data is null;
    }

    public static void IsNotNull(int[] data)
    {
        var result = data is not null;
    }

    public static void LiftedNullTest(int? n)
    {
        var result = n == null;
    }

    public static void LiftedAdd(int? a, int b)
    {
        var result = a + b;
    }

    public static void UserEquals(Money a, Money b)
    {
        var result = a == b;
    }

    public static void UserEqualsNull(Node node)
    {
        var result = node == null;
    }

    public static void RefReassign(int[] data)
    {
        ref int r = ref data[0];
        r = ref data[1];
    }

    public static void UserOperator(Money a, Money b)
    {
        var result = a + b;
    }

    public static void UserConversion(Money a)
    {
        int result = a;
    }

    public static void Coalesce(int[]? a, int[] b)
    {
        var result = a ?? b;
    }

    public static void NullAssignment()
    {
        int[]? result = null;
    }

    public static void ExplicitNumeric(long x)
    {
        var result = (int)x;
    }

    public static void ImplicitNumeric(int x)
    {
        long result = x;
    }

    public static void Identity(int x)
    {
        var result = (int)x;
    }

    public static void ImplicitConstant()
    {
        byte result = 1;
    }

    public static void EnumConversion(Color color)
    {
        var result = (int)color;
    }

    public static void Compound(byte b)
    {
        b += 1;
    }

    public static void Prefix(int x)
    {
        var result = ++x;
    }

    public static void Postfix(int x)
    {
        var result = x--;
    }

    public static void CheckedAdd(int a, int b)
    {
        var result = checked(a + b);
    }

    public static void StructInitializer(int j)
    {
        var result = new Cell { F = j, Auto = 2 };
    }

    public static void NestedInitializer(int j)
    {
        var result = new Outer { Inner = { F = j } };
    }

    public static void DefaultStruct()
    {
        var result = default(Cell);
    }

    public static void ArrayCreation(int n)
    {
        var result = new int[] { n, n + 1 };
    }

    public static void MultiDimensional()
    {
        var result = new int[2, 2];
    }

    public static void MultiDimensionalElement(int[,] grid)
    {
        var result = grid[0, 1];
    }

    public static void DiscardAssignment(int x)
    {
        _ = x;
    }

    public static void Lambda(int x)
    {
        Func<int, int> result = v => v + x;
    }

    public static void SwitchExpression(int x)
    {
        var result = x switch { 0 => 1, _ => 2 };
    }

    public static void With(Cell cell)
    {
        var result = cell with { F = 1 };
    }

    public static unsafe void Dereference(int* p)
    {
        var result = *p;
    }

    public static void Stackalloc()
    {
        Span<int> result = stackalloc int[4];
    }

    public static void Throw(int[]? data)
    {
        var result = data ?? throw new InvalidOperationException();
    }

    public static void DelegateInvoke(Func<int, int> f)
    {
        var result = f(1);
    }

    public static void Invalid()
    {
        var result = Missing(1);
    }

    // --- Statements ---

    public static void MultipleDeclarators()
    {
        int a = 1, b = a;
    }

    public static int ExpressionBody(int x) => x + 1;

    public static void IfElse(int x, int[] data)
    {
        if (x > 0)
            data[0] = 1;
        else
        {
            data[0] = 2;
        }
    }

    public static void Loop(int[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
    }

    public static void SameLocalTwice(int[] data)
    {
        int x = data[0];
        data[1] = x + x;
    }
}
