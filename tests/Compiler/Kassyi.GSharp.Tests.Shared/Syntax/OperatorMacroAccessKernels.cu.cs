// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The context of the operator macros below, which counts the calls of their targets.</summary>
public struct AccessCtx
{
    public int Calls;
}

/// <summary>An operator macro whose target is private to the struct that declares the operator.</summary>
public partial struct HiddenValue
{
    public int V;

    public HiddenValue(int v) => V = v;

    [GpuOperatorMacro(typeof(HiddenValue), nameof(Add))]
    public static HiddenValue operator +(HiddenValue left, HiddenValue right) => throw new GpuOnlyException();

    [GpuDeviceFunction]
    private static void Add(ref AccessCtx ctx, ref HiddenValue result, HiddenValue left, HiddenValue right)
    {
        result.V = left.V + right.V;
        ctx.Calls = ctx.Calls + 1;
    }
}

/// <summary>An operator macro whose target is private to a static class.</summary>
public struct HiddenOpsValue
{
    public int V;

    public HiddenOpsValue(int v) => V = v;

    [GpuOperatorMacro(typeof(HiddenOps), "Multiply")]
    public static HiddenOpsValue operator *(HiddenOpsValue left, HiddenOpsValue right) => throw new GpuOnlyException();
}

public static partial class HiddenOps
{
    [GpuDeviceFunction]
    private static void Multiply(ref AccessCtx ctx, ref HiddenOpsValue result, HiddenOpsValue left, HiddenOpsValue right)
    {
        result.V = left.V * right.V;
        ctx.Calls = ctx.Calls + 1;
    }
}

/// <summary>
/// Operator macros whose targets the code that uses them cannot name in C# (#711): the native code calls them by their
/// C++ names, the managed fallback through an <c>[UnsafeAccessor]</c> stub. <see cref="OperatorMacroAccessTests"/>
/// checks the values on the native target, <c>OperatorMacroAccessManagedTests</c> on the managed fallback.
/// </summary>
public static partial class OperatorMacroAccessKernels
{
    [GpuKernel]
    public static void PrivateTargetsKernel(int[] values, int[] result)
    {
        AccessCtx ctx = default;
        HiddenValue a = new HiddenValue(values[0]);
        HiddenValue c = new HiddenValue(0);
        c = a + a;
        HiddenOpsValue x = new HiddenOpsValue(values[1]);
        HiddenOpsValue z = new HiddenOpsValue(0);
        z = x * x;
        result[0] = c.V;
        result[1] = z.V;
        result[2] = ctx.Calls;
    }

    // Its managed version calls the private targets through stubs of its own
    [GpuDeviceFunction]
    public static int SumOfSquares(ref AccessCtx ctx, int a, int b)
    {
        HiddenOpsValue x = new HiddenOpsValue(a);
        HiddenOpsValue y = new HiddenOpsValue(b);
        HiddenOpsValue xx = new HiddenOpsValue(0);
        HiddenOpsValue yy = new HiddenOpsValue(0);
        xx = x * x;
        yy = y * y;
        HiddenValue s = new HiddenValue(xx.V);
        HiddenValue t = new HiddenValue(yy.V);
        HiddenValue sum = new HiddenValue(0);
        sum = s + t;
        return sum.V;
    }

    [GpuKernel]
    public static void DeviceFunctionKernel(int[] values, int[] result)
    {
        AccessCtx ctx = default;
        result[0] = SumOfSquares(ref ctx, values[0], values[1]);
        result[1] = ctx.Calls;
    }
}
