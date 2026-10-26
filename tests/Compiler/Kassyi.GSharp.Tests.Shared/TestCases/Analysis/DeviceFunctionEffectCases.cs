using Kassyi.GSharp.Core;

namespace EffectCases;

public struct Counter
{
    public int Value;

    public Counter(int start)
    {
        Value = start;
    }

    public void Increment() => Value++;

    public int Peek() => Value;
}

public struct Seeded(int seed)
{
    public int X = seed + 1;
}

public struct InitializerWrites(int[] log)
{
    public int X = Effects.WriteElement(log, 0);
}

public ref struct LogScope
{
    public int[] Log;

    public void Dispose() => Log[0] += 1;
}

public ref struct QuietScope
{
    public int Closed;

    public void Dispose() => Closed = 1;
}

public struct Box
{
    public int[] Data;

    public int First
    {
        get => Data[0];
        set => Data[0] = value;
    }

    public static implicit operator int(Box box)
    {
        box.Data[1] += 1;
        return 0;
    }
}

public static class Effects
{
    private static int s_total;

    // --- None ---

    [GpuDeviceFunction]
    public static int Square(int x) => x * x;

    [GpuDeviceFunction]
    public static int LocalsOnly(int x)
    {
        int sum = 0;
        for (int i = 0; i < x; i++)
        {
            sum += i;
        }

        x += sum;
        return x;
    }

    [GpuDeviceFunction]
    public static int CallsNone(int x)
    {
        int y = Square(x);
        Increment(ref y);
        return System.Math.Max(y, 2);
    }

    [GpuDeviceFunction]
    public static int ReadsMemory(int[] data, int index) => data[index] + 1;

    [GpuDeviceFunction]
    public static int LambdaLocal(int x)
    {
        int captured = 0;
        System.Func<int, int> add = v => captured += v;
        return x;
    }

    [GpuDeviceFunction]
    public static Counter InitializerOnly(int x) => new Counter { Value = x };

    [GpuDeviceFunction]
    public static int QuietUsing()
    {
        using var scope = new QuietScope();
        return 0;
    }

    [GpuDeviceFunction]
    public static int GetterRead(Box box) => box.First;

    // --- RefParameters ---

    [GpuDeviceFunction]
    public static void Increment(ref int value) => value++;

    [GpuDeviceFunction]
    public static void WriteOut(out int value)
    {
        value = 3;
    }

    [GpuDeviceFunction]
    public static void BumpField(ref Counter counter) => counter.Value += 1;

    [GpuDeviceFunction]
    public static void ForwardsRef(ref int value) => Increment(ref value);

    [GpuDeviceFunction]
    public static void CarryIntrinsic(uint a, uint b, ref ulong carry)
    {
        Gpu.AddWithCarry(a, b, ref carry);
    }

    // using (<expression>) disposes the variable the expression names: here the caller's, through ref
    [GpuDeviceFunction]
    public static void CloseByExpression(ref QuietScope scope)
    {
        using (scope)
        {
        }
    }

    // --- Memory ---

    [GpuDeviceFunction]
    public static int WriteElement(int[] data, int index)
    {
        data[index] = 1;
        return 0;
    }

    [GpuDeviceFunction]
    public static int WriteStatic(int x)
    {
        s_total += x;
        return x;
    }

    [GpuDeviceFunction]
    public static int UsesSharedMemory(int x)
    {
        var scratch = new SharedMemory<int>(32);
        return x;
    }

    [GpuDeviceFunction]
    public static void Barrier() => Gpu.SyncThreads();

    [GpuDeviceFunction]
    public static int Atomic(int[] data) => Gpu.AtomicAdd(ref data[0], 1);

    [GpuDeviceFunction]
    public static int CallsMemory(int[] data) => WriteElement(data, 0);

    [GpuDeviceFunction]
    public static int LoggingUsing(int[] log)
    {
        using var scope = new LogScope { Log = log };
        return 0;
    }

    [GpuDeviceFunction]
    public static void SetterWrite(Box box) => box.First = 3;

    [GpuDeviceFunction]
    public static void PropertyIncrement(Box box) => box.First++;

    [GpuDeviceFunction]
    public static void PropertyPrefixDecrement(Box box) => --box.First;

    [GpuDeviceFunction]
    public static int ConversionWrite(Box box)
    {
        int value = box;
        return value;
    }

    [GpuDeviceFunction]
    public static void ForwardsElement(int[] data) => Increment(ref data[0]);

    [GpuDeviceFunction]
    public static int Ping(int x) => x > 0 ? Pong(x - 1) : 0;

    [GpuDeviceFunction]
    public static int Pong(int x) => x > 0 ? Ping(x - 1) : 0;
}
