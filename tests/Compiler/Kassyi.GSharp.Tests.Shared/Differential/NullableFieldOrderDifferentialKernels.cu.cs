#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>An array argument whose element holds a <see cref="System.Nullable{T}"/> of another user struct.</summary>
public struct NullableOrderOuter
{
    public int Tag;
    public NullableOrderInner? Value;
}

/// <summary>The value struct, which only the field of <see cref="NullableOrderOuter"/> names.</summary>
public struct NullableOrderInner
{
    public int A;
    public int B;
}

/// <summary>A struct that only a local of the kernel names, with a nullable field of another struct.</summary>
public struct NullableOrderLocalOuter
{
    public NullableOrderLocalInner? Value;
}

/// <summary>The value struct of <see cref="NullableOrderLocalOuter"/>.</summary>
public struct NullableOrderLocalInner
{
    public int A;
}

/// <summary>
/// Differential kernels for structs with a <c>Nullable&lt;S&gt;</c> field of another user struct: the device needs
/// <c>S</c> defined before the struct whose <c>System_Nullable&lt;S&gt;</c> field holds it (#1888).
/// </summary>
public static partial class NullableFieldOrderDifferentialKernels
{
    public static readonly string[] ReadColumns =
    [
        "tag",
        "has_value",
        "a",
        "b",
        "local",
    ];

    [GpuKernel]
    public static void ReadNullableStructFieldKernel(NullableOrderOuter[] items, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= items.Length)
        {
            return;
        }

        var item = items[i];
        int o = i * 5;
        r[o + 0] = item.Tag;
        r[o + 1] = item.Value.HasValue ? 1 : 0;
        r[o + 2] = item.Value.HasValue ? item.Value.GetValueOrDefault().A : -1;
        r[o + 3] = item.Value.HasValue ? item.Value.GetValueOrDefault().B : -1;

        var local = new NullableOrderLocalOuter();
        if ((item.Tag & 1) == 0)
        {
            local.Value = new NullableOrderLocalInner { A = item.Tag * 3 };
        }

        r[o + 4] = local.Value.HasValue ? local.Value.GetValueOrDefault().A : -7;
    }
}
