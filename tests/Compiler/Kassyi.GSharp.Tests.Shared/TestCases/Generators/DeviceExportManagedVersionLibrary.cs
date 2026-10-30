using Kassyi.GSharp.Core;

namespace DeviceExportManagedVersionCase;

public struct Counter
{
    public int Calls;
}

public struct Value
{
    public int V;

    public Value(int v) => V = v;

    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Add))]
    public static Value operator +(Value left, Value right) => throw new GpuOnlyException();
}

// A library without kernels whose functions expand an operator macro (#711): AddTwice and the internal Twice get a
// managed version, Add and Plain do not
public static partial class Ops
{
    [GpuDeviceFunction]
    public static void Add(ref Counter counter, ref Value result, Value left, Value right)
    {
        result.V = left.V + right.V;
        counter.Calls = counter.Calls + 1;
    }

    [GpuDeviceFunction]
    internal static int Twice(ref Counter counter, int a)
    {
        Value x = new Value(a);
        Value z = new Value(0);
        z = x + x;
        return z.V;
    }

    [GpuDeviceFunction]
    public static int AddTwice(ref Counter counter, int a) => Twice(ref counter, a) + Twice(ref counter, a);

    [GpuDeviceFunction]
    public static int Plain(int a) => a + 1;
}
