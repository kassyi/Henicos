// ReSharper disable All
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The context of <see cref="RouteValue"/>'s operator macro, which counts its calls.</summary>
public struct RouteCtx
{
    public int Calls;
}

public struct RouteValue
{
    public int Value;

    public RouteValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(RouteOps), nameof(RouteOps.Add))]
    public static RouteValue operator +(RouteValue left, RouteValue right) => throw new GpuOnlyException();
}

public static partial class RouteOps
{
    [GpuDeviceFunction]
    public static void Add(ref RouteCtx ctx, ref RouteValue result, RouteValue left, RouteValue right)
    {
        result.Value = left.Value + right.Value;
        ctx.Calls = ctx.Calls + 1;
    }
}

public static partial class RouteExtensions
{
    // An extension method that expands the operator macro, called in its reduced form
    [GpuDeviceFunction]
    public static int AddInto(this ref RouteCtx ctx, int a, int b)
    {
        RouteValue x = new RouteValue(a);
        RouteValue y = new RouteValue(b);
        RouteValue z = new RouteValue(0);
        z = x + y;
        return z.Value;
    }
}

/// <summary>
/// Device functions that expand an operator macro directly or through a call, and the forms that call them: on the
/// managed fallback each one runs from its managed version (#711). <see cref="OperatorMacroRoutingTests"/> checks the
/// values on the native target, <c>OperatorMacroRoutingManagedTests</c> on the managed fallback.
/// </summary>
public static partial class OperatorMacroRoutingKernels
{
    // Expands the operator macro
    [GpuDeviceFunction]
    public static int AddValues(ref RouteCtx ctx, int a, int b)
    {
        RouteValue x = new RouteValue(a);
        RouteValue y = new RouteValue(b);
        RouteValue z = new RouteValue(0);
        z = x + y;
        return z.Value;
    }

    // Expands none itself, but calls AddValues
    [GpuDeviceFunction]
    public static int AddThroughCall(ref RouteCtx ctx, int a, int b) => AddValues(ref ctx, a, b) * 10;

    // Reaches no operator macro: stays the user's method
    [GpuDeviceFunction]
    public static int Twice(int a) => a * 2;

    [GpuKernel]
    public static void TransitiveKernel(int[] values, int[] result)
    {
        RouteCtx ctx = default;
        result[0] = AddThroughCall(ref ctx, values[0], values[1]);
        result[1] = Twice(values[2]);
        result[2] = ctx.Calls;
    }

    // Recursive: the managed version calls itself
    [GpuDeviceFunction]
    public static int SumTo(ref RouteCtx ctx, int n)
    {
        if (n <= 0)
        {
            return 0;
        }

        RouteValue x = new RouteValue(n);
        RouteValue y = new RouteValue(SumTo(ref ctx, n - 1));
        RouteValue z = new RouteValue(0);
        z = x + y;
        return z.Value;
    }

    [GpuKernel]
    public static void RecursionKernel(int[] result)
    {
        RouteCtx ctx = default;
        result[0] = SumTo(ref ctx, 4);
        result[1] = ctx.Calls;
    }

    [GpuKernel]
    public static void ExtensionKernel(int[] values, int[] result)
    {
        RouteCtx ctx = default;
        result[0] = ctx.AddInto(values[0], values[1]);
        result[1] = ctx.Calls;
    }

    // Expands the operator macro with a context of its own
    [GpuDeviceFunction]
    public static int AddOne(int value)
    {
        RouteCtx ctx = default;
        RouteValue x = new RouteValue(value);
        RouteValue y = new RouteValue(1);
        RouteValue z = new RouteValue(0);
        z = x + y;
        return z.Value;
    }

    // The call is inside a StackLinq lambda
    [GpuKernel]
    public static void LambdaKernel(int[] values, int[] result)
    {
        result[0] = values.AsStackEnumerable().Select(v => AddOne(v)).Sum();
    }
}
