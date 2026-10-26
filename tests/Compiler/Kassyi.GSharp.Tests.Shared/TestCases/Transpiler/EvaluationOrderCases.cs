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

    public static void LocalFunctionCall()
    {
        int i = 0;
        int Bump() => i++;
        var result = Bump();
    }
}
