using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct SyncGridScope : IDisposable
{
    public void Dispose()
    {
    }
}

public struct SyncGridPair
{
    public int A;
    public int B;

    public void Deconstruct(out int a, out int b)
    {
        a = A;
        b = B;
    }
}

// Declarations after a return of a Gpu.SyncGrid body: the ones that cannot be split into a declaration and an
// assignment (ref, using, deconstruction, scoped, pattern variables) and the ones that can (out variables, plain locals);
// and goto statements that jump across the gaps of the body
public class SyncGridBodySadPath
{
    [GpuDeviceFunction]
    public static int Read(out int value)
    {
        value = 1;
        return 2;
    }

    [GpuKernel(Cooperative = true)]
    public static void DeclarationsKernel(int[] a, int n)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            ref int before = ref a[0];
            if (tid >= n) return;
            ref int r = ref a[tid];
            using var scope = new SyncGridScope();
            var (p, q) = new SyncGridPair();
            Read(out var o);
            int s = Read(out var o2);
            scoped ref int sr = ref a[1];
            if (n is int pv) a[0] = pv;
            if (Read(out var o3) > 0) a[1] = o3;
            int fine = 1;
            a[tid] = before + r + p + q + o + s + o2 + sr + fine + o3;
        });
    }

    [GpuKernel(Cooperative = true)]
    public static void GotoKernel(int[] a)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid == 0) goto end;
            a[tid] = 1;
            for (int i = 0; i < 2; i++)
            {
                if (i == 1) goto next;
                a[tid] += i;
                next: ;
            }
            end: a[tid] += 2;
        });
    }
}
