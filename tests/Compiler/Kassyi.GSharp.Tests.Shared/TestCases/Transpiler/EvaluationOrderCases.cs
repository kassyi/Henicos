using Kassyi.GSharp.Core;

namespace OrderCases;

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

    public static void LocalFunctionCall()
    {
        int i = 0;
        int Bump() => i++;
        var result = Bump();
    }
}
