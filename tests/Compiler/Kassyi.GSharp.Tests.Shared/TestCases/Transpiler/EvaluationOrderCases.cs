using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace OrderCases;

public struct OrderBox
{
    public int[] Data;

    // Writes memory when it converts
    public static implicit operator int(OrderBox box)
    {
        box.Data[0] += 1;
        return box.Data[1];
    }
}

public struct StructWithPureGetter { public int A; public int B; public int Sum => A + B; }

public struct OrderInner
{
    public int V;

    public readonly int Get() => V;
}

public struct OrderStructWithGetters
{
    public int Value;
    public int ComputedDoubled => Value * 2;
    public int MutatingNext => ++Value;
    public OrderInner ComputedInner => new OrderInner { V = Value };
    public OrderInner MutatingInner => new OrderInner { V = ++Value };
    public OrderInner this[int step] => new OrderInner { V = Value += step };

    // Properties of the implicit this
    public void ImplicitThisMutatingGetterRead() { var result = Value + MutatingNext; }
    public void ImplicitThisReceiverGetterRead() { var result = Value + MutatingInner.V; }
    public void ImplicitThisPureGetterRead() { var result = Value + ComputedDoubled; }
}

public struct OrderView
{
    public int[] Data;
    public int Stored;

    // Writes memory
    public OrderInner Touch
    {
        get
        {
            Data[0] += 1;
            return new OrderInner { V = 1 };
        }
    }

    // The getter writes memory, the setter only the struct
    public int Mark
    {
        get
        {
            Data[0] += 1;
            return Stored;
        }
        set => Stored = value;
    }

    // The setter reads memory
    public int Load
    {
        readonly get => Stored;
        set => Stored = value + Data[0];
    }

    // The setter writes memory
    public int Poke
    {
        readonly get => Stored;
        set
        {
            Data[0] += 1;
            Stored = value;
        }
    }
}
// The builder of a collection expression writes memory: a collection expression is evaluated, then its builder runs
[CollectionBuilder(typeof(OrderBagBuilder), nameof(OrderBagBuilder.Create))]
public struct OrderBag
{
    public int Count;

    public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw new System.NotSupportedException();
}

public static class OrderBagBuilder
{
    public static readonly int[] Sink = new int[1];

    [GpuDeviceFunction]
    public static OrderBag Create(System.ReadOnlySpan<int> values)
    {
        Sink[0] += values.Length;
        OrderBag bag;
        bag.Count = values.Length;
        return bag;
    }

    [GpuDeviceFunction]
    public static int Weigh(OrderBag bag) => bag.Count;
}

public struct OrderPair
{
    public int X;
    public int Y;

    // A field of the struct that a member reads is a field of this
    public int ThisFieldRead()
    {
        var result = X;
        return result;
    }
}

// The field keyword reads the backing field of the property, a field of this; Bump writes it through the setter
public struct OrderFieldKeyword
{
    public int FieldRead
    {
        get
        {
            var result = field;
            return result;
        }
        set => field = value;
    }

    public int FieldThenWrite
    {
        get
        {
            var result = field + Bump(ref this);
            return result;
        }
        set => field = value;
    }

    [GpuDeviceFunction]
    public static int Bump(ref OrderFieldKeyword order)
    {
        order.FieldThenWrite = 100;
        return 1;
    }
}

// Each case method ends with 'var result = <expression>;': the expression whose operands are analysed
public static class Order
{
    [GpuDeviceFunction]
    public static int Next(ref int counter) => counter++;

    [GpuDeviceFunction]
    public static int Sub(int a, int b) => a - b;

    [GpuDeviceFunction]
    public static int Combine3(int a, int b, int c) => a * 100 + b * 10 + c;

    [GpuDeviceFunction]
    public static int Store(int[] data, int value)
    {
        data[0] = value;
        return value;
    }

    [GpuDeviceFunction]
    public static int Pure(int x) => x + 1;

    [GpuDeviceFunction]
    public static int Apply(System.Func<int, int> f, int x) => x;

    [GpuDeviceFunction]
    public static int ReadRef(ref int value) => value;

    [GpuDeviceFunction]
    public static unsafe int SetThrough(int* target, int value)
    {
        *target = value;
        return 0;
    }

    [GpuDeviceFunction]
    public static int Invoke(System.Func<int, int> f, int value) => f(value);

    [GpuDeviceFunction]
    public static ref int Choose(ref int a, ref int b, bool first)
    {
        if (first)
        {
            return ref a;
        }

        return ref b;
    }

    public static void ExplicitConversion(OrderBox box)
    {
        var result = (int)box;
    }

    public static void ImplicitConversion(OrderBox box)
    {
        int result = box;
    }

    // --- Arguments that interfere ---

    public static void TwoNextCalls()
    {
        int i = 0;
        var result = Sub(Next(ref i), Next(ref i));
    }

    public static void LocalThenRefWrite()
    {
        int t = 7;
        var result = Sub(t, Next(ref t));
    }

    public static void MemoryReadThenWrite(int[] d)
    {
        var result = Sub(d[0], Store(d, 5));
    }

    public static void IncrementThenRead()
    {
        int i = 0;
        var result = Sub(i++, i);
    }

    public static void InterferenceBeforeLastArgument(int j)
    {
        int i = 0;
        var result = Combine3(Next(ref i), i, j);
    }

    public static void BinaryOperands()
    {
        int i = 0;
        var result = Next(ref i) - Next(ref i);
    }

    // A ref argument is read by its callee while the call is evaluated
    public static void WriteThenRefRead()
    {
        int i = 0;
        var result = Sub(Next(ref i), ReadRef(ref i));
    }

    // x is written through another name
    public static void RefLocalAlias()
    {
        int x = 0;
        ref int r = ref x;
        var result = Sub(x, Next(ref r));
    }

    public static unsafe void PointerAlias()
    {
        int x = 0;
        int* p = &x;
        var result = Sub(x, SetThrough(p, 5));
    }

    // Choose returns a reference to x, so x has a second name
    public static void RefReturnAlias()
    {
        int x = 0;
        int y = 0;
        var result = Sub(x, Next(ref Choose(ref x, ref y, true)));
    }

    public static void CapturedAlias()
    {
        int x = 0;
        System.Func<int, int> f = v => x = v;
        var result = Sub(x, Invoke(f, 5));
    }

    // --- Arguments that do not interfere ---

    public static void PureAndElement(int[] d, int x, int i)
    {
        var result = Sub(Pure(x), d[i]);
    }

    public static void TwoElements(int[] d, int[] e, int i)
    {
        var result = Sub(d[i], e[i]);
    }

    public static void OneSideEffect()
    {
        int i = 0;
        var result = Sub(Next(ref i), 5);
    }

    public static void MathWithOneSideEffect()
    {
        int i = 0;
        var result = System.Math.Max(Next(ref i), 5);
    }

    public static void LambdaIsNotEvaluated()
    {
        int i = 0;
        var result = Apply(v => v + i, Next(ref i));
    }

    // A lambda that writes nothing next to a read of the local it is given
    public static void PureLambdaCall(int v)
    {
        var twice = (int x) => x * 2;
        var result = Sub(v, twice(v));
    }

    public static void RefArgumentsOnly()
    {
        int i = 0;
        int j = 0;
        var result = Sub(ReadRef(ref i), ReadRef(ref j));
    }

    // --- Single expressions ---

    public static void Constant()
    {
        var result = 5 * 3;
    }

    public static void ThreadIndexRead()
    {
        var result = ThreadIndex.X;
    }

    public static void LocalRead()
    {
        int i = 0;
        var result = i;
    }

    public static void Increment()
    {
        int i = 0;
        var result = i++;
    }

    public static void ElementRead(int[] d, int i)
    {
        var result = d[i];
    }

    public static void RefCall()
    {
        int i = 0;
        var result = Next(ref i);
    }

    public static void MemoryCall(int[] d)
    {
        var result = Store(d, 1);
    }

    public static void PureCall(int x)
    {
        var result = Pure(x);
    }

    public static void RefParameterRead(ref int p)
    {
        var result = p;
    }

    public static void ConditionalUnion(int[] d, int c)
    {
        int i = 0;
        var result = c > 0 ? Next(ref i) : d[0];
    }

    // A lambda that increments the local that the other argument reads
    public static void CapturingLambdaCall()
    {
        int i = 0;
        var bump = () => i++;
        var result = Sub(i, bump());
    }

    private static int s_total;

    public static void StaticFieldRead()
    {
        var result = s_total;
    }

    public static void RefLocalRead(int[] d)
    {
        ref int r = ref d[0];
        var result = r;
    }

    public static void SharedMemoryRead()
    {
        var shared = new SharedMemory<int>(32);
        var result = shared[ThreadIndex.X];
    }

    public static unsafe void PointerRead(int* p)
    {
        var result = *p;
    }

    public static void StructLocalFieldRead()
    {
        var pair = new OrderPair();
        var result = pair.X;
    }

    public static void OutVarArgument()
    {
        var result = Split(out var y);
    }

    public static void WithExpression()
    {
        var pair = new OrderPair();
        var result = pair with { X = 1 };
    }

    [GpuDeviceFunction]
    public static int Split(out int y)
    {
        y = 1;
        return 0;
    }

    public static void ArrayLengthAndElement(int[] values, int tid) { var result = values.Length * 1000 + values[tid]; }
    public static void StructPureGetterAndConstant(StructWithPureGetter s) { var result = 100 - s.Sum; }
    public static void PureStructGetterRead(OrderStructWithGetters s) { var result = s.Value + s.ComputedDoubled; }
    public static void MutatingStructGetterRead(OrderStructWithGetters s) { var result = s.Value + s.MutatingNext; }
    public static void ReceiverMutatingGetterRead(OrderStructWithGetters s)
    {
        var result = s.Value + s.MutatingInner.V;
    }

    public static void ReceiverMutatingGetterAlone(OrderStructWithGetters s) { var result = s.MutatingInner.V; }
    public static void ReceiverPureGetterRead(OrderStructWithGetters s) { var result = s.Value + s.ComputedInner.V; }
    public static void ReceiverMutatingIndexerRead(OrderStructWithGetters s) { var result = s.Value + s[1].V; }
    public static void ReceiverMutatingGetterCall(OrderStructWithGetters s)
    {
        var result = s.Value + s.MutatingInner.Get();
    }

    public static void ReceiverMemoryWritingGetterRead(OrderView v, int[] d) { var result = d[0] + v.Touch.V; }
    public static void CompoundMemoryWritingGetter(OrderView v, int[] d) { var result = d[0] + (v.Mark += 1); }
    public static void SetterReadingMemory(OrderView v, int[] d) { var result = Sub(v.Load = 1, Store(d, 5)); }
    public static void InitializerSetterWritingMemory(int[] d)
    {
        var result = d[0] + new OrderView { Data = d, Poke = 1 }.Stored;
    }

    public static void WithSetterWritingMemory(OrderView v, int[] d)
    {
        var result = d[0] + (v with { Poke = 1 }).Stored;
    }

    public static void CollectionBuilderWritingMemory(int[] d) { var result = d[0] + OrderBagBuilder.Weigh([1, 2]); }
public static void LocalFunctionCall()
    {
        int i = 0;
        int Bump() => i++;
        var result = Bump();
    }

    // --- A check that may throw next to a write to memory: interfere ---

    public static void WriteThenLength(int[] d, int[] a) { var result = Store(d, 5) + a.Length; }
    public static void LengthThenWrite(int[] d, int[] a) { var result = a.Length + Store(d, 5); }
    public static void ElementWriteThenLength(int[] d, int[] a) { var result = (d[1] = 5) + a.Length; }
    public static void ElementIncrementThenLength(int[] d, int[] a) { var result = d[1]++ + a.Length; }
    public static void WriteThenLengthArgument(int[] d, int[] a) { var result = Sub(Store(d, 5), a.Length); }
    public static void WriteThenDivision(int[] d, int x) { var result = Store(d, 5) + 10 / x; }
    public static void WriteThenNullableValue(int[] d, int? n) { var result = Store(d, 5) + (int)n; }
    public static void WriteThenNotNull(int[] d, int[] a) { var result = Store(d, 5) + Gpu.NotNull(a).Length; }
    public static void DivisionThenWrite(int[] d, int x) { var result = 10 / x + Store(d, 5); }

    // --- A check next to a read, or next to a write to a local: do not interfere ---

    public static void ReadThenLength(int[] d, int[] a) { var result = d[1] + a.Length; }
    public static void LengthThenRead(int[] d, int[] a) { var result = a.Length + d[1]; }

    public static void LocalWriteThenLength(int[] a)
    {
        int i = 0;
        var result = i++ + a.Length;
    }

    public static void WriteThenStackArrayLength(int[] d)
    {
        int[] s = new int[4];
        var result = Store(d, 5) + s.Length;
    }

    public static void WriteThenSpanLength(int[] d, System.Span<int> s) { var result = Store(d, 5) + s.Length; }
    public static void WriteThenConstantDivision(int[] d, int x) { var result = Store(d, 5) + x / 2; }

    // --- The target of an assignment: the expression is the target ---

    public static void ElementTarget(int[] d, int i) { var result = d[i]; }
    public static void EnumElementTarget(System.DayOfWeek[] d, int i) { var result = d[i]; }
    public static void StructElementTarget(OrderInner[] d, int i) { var result = d[i]; }

    public static void StackArrayElementTarget(int i)
    {
        int[] s = new int[4];
        var result = s[i];
    }
    public static void ElementTargetWithCheckedIndex(int[] d, int[] a) { var result = d[a.Length]; }
}
