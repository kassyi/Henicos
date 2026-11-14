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
}
