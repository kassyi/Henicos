using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public struct StackScopeLoweringGuard : IDisposable
{
    public uint[] Arr;
    public int I;

    public void Dispose()
    {
        Arr[I] += 0x100000u;
    }
}

public static class StackScopeLoweringCases
{
    [GpuKernel]
    public static void LabeledUsingDeclarationKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        Scoped:
        using var gd = new StackScopeLoweringGuard { Arr = r, I = t };
        if (r[t] == 0u)
        {
            r[t] = 1u;
            goto Scoped;
        }

        r[t] += Warp.ActiveMask();
    }
}
