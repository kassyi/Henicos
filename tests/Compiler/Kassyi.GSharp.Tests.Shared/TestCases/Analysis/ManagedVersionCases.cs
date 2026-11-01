using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace ManagedVersionCases;

public struct Ctx
{
    public int Calls;
}

public struct Value
{
    public int V;

    public Value(int v) => V = v;

    [GpuOperatorMacro(typeof(Functions), nameof(Functions.Add))]
    public static Value operator +(Value left, Value right) => throw new GpuOnlyException();

    // Not an operator macro: runs as written
    public static Value operator -(Value left, Value right) => new Value(left.V - right.V);
}

// The inputs of ManagedVersionAnalysisTests (#711, #730)
public static partial class Functions
{
    [GpuDeviceFunction]
    public static void Add(ref Ctx ctx, ref Value result, Value left, Value right)
    {
        result.V = left.V + right.V;
    }

    // Expands the operator macro itself
    [GpuDeviceFunction]
    public static int Direct(ref Ctx ctx, int a)
    {
        Value x = new Value(a);
        Value z = new Value(0);
        z = x + x;
        return z.V;
    }

    // Reaches Direct through a call
    [GpuDeviceFunction]
    public static int Transitive(ref Ctx ctx, int a) => Direct(ref ctx, a) + 1;

    // Reaches Transitive through a call
    [GpuDeviceFunction]
    public static int TwoLevels(ref Ctx ctx, int a) => Transitive(ref ctx, a) * 2;

    // A user-defined operator without [GpuOperatorMacro] and plain arithmetic: nothing to rewrite
    [GpuDeviceFunction]
    public static int Plain(int a)
    {
        Value x = new Value(a);
        Value z = new Value(0);
        z = x - x;
        return z.V + a;
    }

    // Calls only Plain
    [GpuDeviceFunction]
    public static int CallsPlain(int a) => Plain(a) * 3;

    // A cycle that reaches no operator macro
    [GpuDeviceFunction]
    public static int Ping(int a) => a > 0 ? Pong(a - 1) : 0;

    [GpuDeviceFunction]
    public static int Pong(int a) => a > 0 ? Ping(a - 1) : 0;

    // A cycle that reaches one: both members need a version
    [GpuDeviceFunction]
    public static int Tick(ref Ctx ctx, int a) => a > 0 ? Tock(ref ctx, a - 1) : 0;

    [GpuDeviceFunction]
    public static int Tock(ref Ctx ctx, int a) => a > 0 ? Tick(ref ctx, a - 1) : Direct(ref ctx, a);

    // Reaches a barrier: it has a state machine, whose body is rewritten instead
    [GpuDeviceFunction]
    public static int Suspends(ref Ctx ctx, int a)
    {
        Value x = new Value(a);
        Value z = new Value(0);
        z = x + x;
        Gpu.SyncThreads();
        return z.V;
    }

    // Names a function that needs a version as a method group only
    [GpuDeviceFunction]
    public static void PassesMethodGroup()
    {
        Gpu.ForUnroll(0, 2, AddAt);
    }

    [GpuDeviceFunction]
    public static void AddAt(int i)
    {
        Ctx ctx = default;
        Value x = new Value(i);
        Value z = new Value(0);
        z = x + x;
    }

    // An extension method that expands the operator macro, and a function that calls it in the reduced form
    [GpuDeviceFunction]
    public static int CallsExtension(ref Ctx ctx, int a) => ctx.Doubled(a);

    // StackLinq float and double Sum calls require a managed version that rewrites Sum to SumSequential (#730)
    [GpuDeviceFunction]
    public static float FloatSumDirect(float[] data) => data.AsStackEnumerable().Sum();

    [GpuDeviceFunction]
    public static double DoubleSumSelector(double[] data) => data.AsStackEnumerable().Sum(x => x * 2.0);

    [GpuDeviceFunction]
    public static float CallsFloatSum(float[] data) => FloatSumDirect(data) + 1f;

    // Integer StackLinq Sum is associative in 32-bit two's-complement arithmetic, so it needs no managed version
    [GpuDeviceFunction]
    public static int IntSumPlain(int[] data) => data.AsStackEnumerable().Sum();

    // A reduced extension method group passed as a delegate (#940)
    [GpuDeviceFunction]
    public static void PassesExtensionMethodGroup(int[] x)
    {
        Gpu.ForUnroll(0, 2, x.AddToIndex);
    }

    // A generic reduced extension method group whose receiver type depends on the inferred type argument (#940)
    [GpuDeviceFunction]
    public static void PassesGenericExtensionMethodGroup(int[] x)
    {
        Gpu.ForUnroll(0, 2, x.AddGeneric);
    }
}

public static partial class Extensions
{
    [GpuDeviceFunction]
    public static int Doubled(this ref Ctx ctx, int a) => Functions.Direct(ref ctx, a);

    [GpuDeviceFunction]
    public static void AddToIndex(this int[] x, int i)
    {
        Ctx ctx = default;
        Value vx = new Value(x[0]);
        Value vy = new Value(i);
        Value z = new Value(0);
        z = vx + vy;
    }

    [GpuDeviceFunction]
    public static void AddGeneric<T>(this T[] x, int i) where T : unmanaged
    {
        Ctx ctx = default;
        Value vx = new Value(x.Length);
        Value vy = new Value(i);
        Value z = new Value(0);
        z = vx + vy;
    }
}

// A class that is not partial around the class of a device function: no managed version is written into it, so
// nothing calls one (#941)
public class Outer
{
    public static partial class Inner
    {
        [GpuDeviceFunction]
        public static int Nested(ref Ctx ctx, int a) => Functions.Direct(ref ctx, a);
    }
}

public static partial class Callers
{
    // Needs a managed version through Direct; calls Outer.Inner.Nested as the user wrote it
    [GpuDeviceFunction]
    public static int CallsNested(ref Ctx ctx, int a) => Outer.Inner.Nested(ref ctx, a) + Functions.Direct(ref ctx, a);
}
