#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct BodyDepInner
{
    public int Value;

    public BodyDepInner(int v)
    {
        Value = (v * 3) + 1;
    }
}

public struct BodyDepScale
{
    public int Factor;

    public BodyDepScale(int v)
    {
        Factor = (v & 7) + 2;
    }
}

public struct BodyDepSeed
{
    public int Value;

    public BodyDepSeed(int v)
    {
        Value = v + 40;
    }
}

public struct BodyDepOuter
{
    public int V;
    public int Seed = new BodyDepSeed(2).Value;

    public BodyDepOuter(int v)
    {
        var inner = new BodyDepInner(v);
        V = inner.Value + 1;
    }

    public int Scaled => new BodyDepScale(V).Factor * (V & 0xFF);
}

public struct BodyDepBox<T>
    where T : unmanaged
{
    public int V;

    public BodyDepBox(int v)
    {
        var inner = new BodyDepInner(v + 5);
        V = inner.Value - v;
    }
}

public struct BodyDepChainBottom
{
    public int Value;

    public BodyDepChainBottom(int v)
    {
        Value = v ^ 0x55;
    }
}

public struct BodyDepChainMiddle
{
    public int Value;

    public BodyDepChainMiddle(int v)
    {
        Value = new BodyDepChainBottom(v).Value + 7;
    }
}

public struct BodyDepChainTop
{
    public int Value;

    public BodyDepChainTop(int v)
    {
        Value = new BodyDepChainMiddle(v * 2).Value;
    }
}

// A struct the device cannot define (reference type fields), named by a constructor only for its constant
public struct BodyDepHostConfig
{
    public const int DefaultValue = 7;
    public string Name;
    public object Payload;
}

public struct BodyDepConstantUser
{
    public int X;

    public BodyDepConstantUser(int x)
    {
        X = x + BodyDepHostConfig.DefaultValue;
    }
}

/// <summary>
/// Differential kernels for structs that only the constructor, member initializer or computed property of another
/// struct creates: the kernel never names them, so their definitions and constructor functions come from the bodies
/// that create them (#1158).
/// </summary>
public static partial class StructBodyDependencyDifferentialKernels
{
    public static readonly string[] CreatedOnlyInBodiesColumns =
    [
        "outer_v",
        "outer_seed",
        "outer_scaled",
        "generic_box_v",
        "chain_top_v",
        "constant_user_x",
    ];

    [GpuKernel]
    public static void StructCreatedOnlyInBodiesKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int x = inputs[tid] & 0xFFFF;
        var outer = new BodyDepOuter(x);
        var box = new BodyDepBox<long>(x);
        var top = new BodyDepChainTop(x);

        int o = tid * 6;
        r[o + 0] = outer.V;
        r[o + 1] = outer.Seed;
        r[o + 2] = outer.Scaled;
        r[o + 3] = box.V;
        r[o + 4] = top.Value;
        r[o + 5] = new BodyDepConstantUser(x).X;
    }
}
