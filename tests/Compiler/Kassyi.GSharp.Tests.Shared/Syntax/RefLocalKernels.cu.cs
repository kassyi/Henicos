// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels with ref locals: the declaration of one, its rebinding, a conditional ref, a ref readonly local and
/// <c>foreach (ref ...)</c>. A ref local is a pointer in the generated C++.
/// </summary>
public static partial class RefLocalKernels
{
    // A ref local of a local variable
    [GpuKernel]
    public static void AliasOfLocalKernel(int[] r)
    {
        int w = r[0];
        ref int alias = ref w;
        alias += 1;
        r[0] = w;
    }

    // A ref local of an array element
    [GpuKernel]
    public static void AliasOfElementKernel(int[] r)
    {
        ref int e = ref r[1];
        e = 40;
        e++;
    }

    // a = ref y; binds the local to another variable
    [GpuKernel]
    public static void RebindKernel(int[] r)
    {
        int x = 1;
        int y = 2;
        ref int a = ref x;
        a = ref y;
        a = 5;
        r[0] = x;
        r[1] = y;
    }

    // ref (c ? ref a : ref b)
    [GpuKernel]
    public static void ConditionalKernel(int[] r, int c)
    {
        int a = 1;
        int b = 2;
        ref int t = ref (c != 0 ? ref a : ref b);
        t = 9;
        r[0] = a;
        r[1] = b;
    }

    // A ref readonly local sees the value that the element gets through another path
    [GpuKernel]
    public static void RefReadOnlyKernel(int[] r)
    {
        ref readonly int v = ref r[0];
        r[0] = 7;
        r[1] = v;
    }

    // foreach (ref ...) over a span of one element of each thread
    [GpuKernel]
    public static void ForEachRefSpanKernel(int[] r)
    {
        int t = ThreadIndex.X;
        Span<int> mine = r.AsSpan(t, 1);
        foreach (ref int e in mine)
        {
            e += 1;
        }
    }

    // foreach (ref ...) over the span of an array, with continue and break
    [GpuKernel]
    public static void ForEachRefArrayKernel(int[] r)
    {
        foreach (ref int e in r.AsSpan())
        {
            if (e == 3)
            {
                continue;
            }

            if (e == 5)
            {
                break;
            }

            e *= 2;
        }
    }

    // A goto jumps past the declaration of a ref local in a nested block
    [GpuKernel]
    public static void GotoPastRefLocalKernel(int[] r, int flag)
    {
        if (flag == 0)
        {
            goto skip;
        }

        {
            ref int a = ref r[0];
            a = 5;
        }

    skip:
        r[1] = 7;
    }

    // A ref local initialized from a ref-returning device function and from a ref-returning local function
    [GpuDeviceFunction]
    public static ref int PickDevice(ref int x, ref int y, int c)
    {
        if (c != 0)
        {
            return ref x;
        }

        return ref y;
    }

    [GpuKernel]
    public static void RefFromRefReturnKernel(int[] r, int c)
    {
        int a = 1;
        int b = 2;
        ref int t = ref PickDevice(ref a, ref b, c);
        t = 9;
        ref int u = ref Pick(ref a, ref b, c);
        u += 100;
        r[0] = a;
        r[1] = b;

        ref int Pick(ref int x, ref int y, int cc)
        {
            if (cc != 0)
            {
                return ref x;
            }

            return ref y;
        }
    }

    // A ref local that one switch section declares and another assigns: the pointer is declared before the switch
    [GpuKernel]
    public static void SharedRefLocalAcrossSectionsKernel(int[] r, int mode)
    {
        int x = 1;
        int y = 2;
        switch (mode)
        {
            case 0:
                ref int a = ref x;
                a = 10;
                break;
            default:
                a = ref y;
                a = 20;
                break;
        }

        r[0] = x;
        r[1] = y;
    }

    // A ref local of a span: the pointer points to the view
    [GpuKernel]
    public static void RefToSpanLocalKernel(int[] r)
    {
        Span<int> view = r;
        ref Span<int> alias = ref view;
        alias[0] = 7;
        r[1] = alias.Length;
    }
}
