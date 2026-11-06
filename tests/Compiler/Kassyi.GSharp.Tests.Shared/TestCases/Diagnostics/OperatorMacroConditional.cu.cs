using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class FlagOps
{
    [GpuDeviceFunction]
    public static void And(ref Flag result, Flag a, Flag b) => result.V = (a.V & b.V) | 4;

    [GpuDeviceFunction]
    public static void Or(ref Flag result, Flag a, Flag b) => result.V = a.V | b.V | 4;
}

// && and || call & and | only after operator false and operator true of the left operand (C# spec §12.15)
public struct Flag
{
    public int V;

    [GpuOperatorMacro(typeof(FlagOps), nameof(FlagOps.And))]
    public static Flag operator &(Flag a, Flag b) => new Flag { V = (a.V & b.V) | 4 };

    [GpuOperatorMacro(typeof(FlagOps), nameof(FlagOps.Or))]
    public static Flag operator |(Flag a, Flag b) => new Flag { V = a.V | b.V | 4 };

    public static bool operator true(Flag f) => f.V != 0;

    public static bool operator false(Flag f) => f.V == 0;
}

public static partial class OperatorMacroConditionalKernels
{
    // GSH0003: the expansion would drop the test of operator false / true and always evaluate the right operand
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConditionalKernel(int[] output)
    {
        Flag a = new Flag { V = 0 };
        Flag b = new Flag { V = 1 };
        Flag c = default;
        c = a && b;
        output[0] = c.V;
        c = b || a;
        output[1] = c.V;
        c = (a && b) & b;
        output[2] = c.V;
    }

    // & and | alone evaluate both operands in C# too, so they expand
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NonConditionalKernel(int[] output)
    {
        Flag a = new Flag { V = 0 };
        Flag b = new Flag { V = 1 };
        Flag c = default;
        c = a & b;
        output[0] = c.V;
        c = a | b;
        output[1] = c.V;
    }
}
