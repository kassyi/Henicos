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
        "nullable_as", "nullable_other"
    ];

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
        int o = tid * 8;
        r[o + 0] = inputs is int[] ? 1 : 0;
        r[o + 1] = maybe is int ? 1 : 0;
        r[o + 2] = maybe is not null ? 1 : 0;
        r[o + 3] = inputs is { } ? 1 : 0;
        r[o + 4] = inputs is not int[] ? 1 : 0;
        r[o + 5] = (inputs as int[]).Length > 0 ? 1 : 0;
        int? same = maybe as int?;
        r[o + 6] = same.HasValue ? (same.Value + 1) : -1;
        r[o + 7] = maybe is long ? 1 : 0;
    }
}
