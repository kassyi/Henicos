#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>An interface that a struct implements, so that a value of the struct boxes to it.</summary>
public interface IDiffTypeTestShape
{
}

/// <summary>A struct whose type tests are decided by its type.</summary>
public struct DiffTypeTestPoint : IDiffTypeTestShape
{
    public int X;
}

/// <summary>Differential kernels for <c>is</c> on a value, which C# decides by the type of the value.</summary>
public static partial class TypeTestDifferentialKernels
{
    public static readonly string[] Columns =
        ["same_type", "wider_type", "interface", "object", "nullable", "other_struct", "order"];

    public static readonly string[] NullColumns =
    [
        "array_is", "nullable_is", "nullable_not_null", "array_empty_pattern", "array_not_pattern", "as_array",
        "nullable_as", "nullable_other", "declare_array", "declare_nullable", "declare_value", "declare_not"
    ];

    public static readonly string[] ScopeColumns = ["across_barrier", "in_switch"];

    [GpuDeviceFunction]
    public static int[] NextArray(int[] values, ref int calls)
    {
        calls++;
        return values;
    }

    [GpuDeviceFunction]
    public static int Step(ref int k)
    {
        k++;
        return k;
    }

    [GpuKernel]
    public static void TypeTestKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        var point = new DiffTypeTestPoint { X = v };
        int o = tid * 7;
        int k = v;
        r[o + 0] = v is int ? 1 : 0;
        r[o + 1] = v is long ? 1 : 0;
        r[o + 2] = point is IDiffTypeTestShape ? 1 : 0;
        r[o + 3] = point is object ? 1 : 0;
        r[o + 4] = v is int? ? 1 : 0;
        r[o + 5] = point is DiffTypeTestPoint ? 1 : 0;
        // The operand is evaluated, as in C#
        r[o + 6] = (Step(ref k) is int ? 100 : 0) + k;
    }

    // An array and a nullable are null or have a value, so the tests on them are null tests (inputs is never null here)
    [GpuKernel]
    public static void NullTypeTestKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int? maybe = v % 2 == 0 ? v : null;
        int o = tid * 12;
        r[o + 0] = inputs is int[] ? 1 : 0;
        r[o + 1] = maybe is int ? 1 : 0;
        r[o + 2] = maybe is not null ? 1 : 0;
        r[o + 3] = inputs is { } ? 1 : 0;
        r[o + 4] = inputs is not int[] ? 1 : 0;
        r[o + 5] = (inputs as int[]).Length > 0 ? 1 : 0;
        int? same = maybe as int?;
        r[o + 6] = same.HasValue ? (same.Value + 1) : -1;
        r[o + 7] = maybe is long ? 1 : 0;
        // The variable gets the value when the pattern holds; the operand is evaluated once
        int k = 0;
        r[o + 8] = (NextArray(inputs, ref k) is int[] seen) ? seen.Length + k : -1;
        r[o + 9] = maybe is int unwrapped ? unwrapped + 1 : -1;
        r[o + 10] = v is int plain ? plain + 2 : -1;
        if (maybe is not int absent)
        {
            r[o + 11] = -7;
            return;
        }

        r[o + 11] = absent + 3;
    }

    // A pattern variable that a barrier is crossed with (the CPU runs the threads as state machines), and one that is
    // declared and used in a switch section. Every thread reaches the barrier: the launch covers the rows exactly.
    [GpuKernel]
    public static void PatternScopeKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int v = inputs[tid] % 1000;
        int? maybe = v % 2 == 0 ? v : null;
        if (maybe is not int kept)
        {
            kept = -1;
        }

        Gpu.SyncThreads();
        int o = tid * 2;
        r[o] = kept + 1;
        switch (v % 3)
        {
            case 0:
                r[o + 1] = maybe is int zero ? zero : -1;
                break;
            case 1:
                r[o + 1] = maybe is int one ? one + 10 : -2;
                break;
            default:
                r[o + 1] = -3;
                break;
        }
    }
}
