// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A resource that appends its <see cref="Id"/> to <see cref="Log"/> when it is disposed.</summary>
/// <remarks><c>Log[0]</c> counts the entries; entry <c>n</c> is at <c>Log[n]</c>.</remarks>
public struct UsingRecorder : IDisposable
{
    public int[] Log;
    public int Id;

    public void Dispose()
    {
        Log[0] = Log[0] + 1;
        Log[Log[0]] = Id;
    }
}

/// <summary>A <c>ref struct</c> with a <c>Dispose()</c> method and no interface, which C# also accepts in a <c>using</c>.</summary>
public ref struct UsingPatternRecorder
{
    public int[] Log;
    public int Id;

    public void Dispose() => Log[++Log[0]] = Id;
}

public static partial class UsingDisposalKernels
{
    [GpuDeviceFunction]
    public static void Note(int[] log, int value)
    {
        log[0] = log[0] + 1;
        log[log[0]] = value;
    }

    [GpuDeviceFunction]
    public static int NoteAndGet(int[] log, int value)
    {
        Note(log, value);
        return value + 1;
    }

    [GpuKernel]
    public static void UsingStatementKernel(int[] log)
    {
        using (var resource = new UsingRecorder { Log = log, Id = 1 })
        {
            Note(log, 10);
        }

        Note(log, 20);
    }

    [GpuKernel]
    public static void UsingEarlyReturnKernel(int[] log, int stop)
    {
        using (var resource = new UsingRecorder { Log = log, Id = 1 })
        {
            Note(log, 10);
            if (stop == 1)
            {
                return;
            }

            Note(log, 11);
        }

        Note(log, 20);
    }

    [GpuKernel]
    public static void UsingInLoopKernel(int[] log)
    {
        for (int i = 0; i < 4; i++)
        {
            using (var resource = new UsingRecorder { Log = log, Id = 100 + i })
            {
                if (i == 1)
                {
                    continue;
                }

                if (i == 2)
                {
                    break;
                }

                Note(log, i);
            }
        }

        Note(log, 20);
    }

    [GpuKernel]
    public static void NestedUsingKernel(int[] log)
    {
        using (var outer = new UsingRecorder { Log = log, Id = 1 })
        using (var inner = new UsingRecorder { Log = log, Id = 2 })
        {
            Note(log, 10);
        }

        using (UsingRecorder first = new UsingRecorder { Log = log, Id = 3 }, second = new UsingRecorder { Log = log, Id = 4 })
        {
            Note(log, 11);
        }
    }

    [GpuKernel]
    public static void UsingDeclarationKernel(int[] log, int stop)
    {
        {
            using var first = new UsingRecorder { Log = log, Id = 1 };
            using var second = new UsingRecorder { Log = log, Id = 2 };
            Note(log, 10);
            if (stop == 1)
            {
                return;
            }

            Note(log, 11);
        }

        Note(log, 20);
    }

    [GpuKernel]
    public static void UsingDeclarationInLoopKernel(int[] log)
    {
        for (int i = 0; i < 3; i++)
        {
            using var resource = new UsingRecorder { Log = log, Id = 100 + i };
            if (i == 1)
            {
                continue;
            }

            Note(log, i);
        }
    }

    [GpuKernel]
    public static void PatternDisposeKernel(int[] log)
    {
        using (var resource = new UsingPatternRecorder { Log = log, Id = 7 })
        {
            Note(log, 10);
        }

        using var declared = new UsingPatternRecorder { Log = log, Id = 8 };
        Note(log, 11);
    }

    [GpuKernel]
    public static void UsingExpressionKernel(int[] log)
    {
        var resource = new UsingRecorder { Log = log, Id = 1 };
        using (resource)
        {
            Note(log, 10);
        }
    }

    [GpuDeviceFunction]
    public static int DisposeAfterReturnValue(int[] log)
    {
        using var resource = new UsingRecorder { Log = log, Id = 1 };
        return NoteAndGet(log, 10);
    }

    [GpuKernel]
    public static void DeviceFunctionUsingKernel(int[] log)
    {
        int value = DisposeAfterReturnValue(log);
        Note(log, value);
    }

    // The CPU dialects lower a kernel with a barrier into a state machine; the using ends before the barrier
    [GpuKernel]
    public static void UsingBeforeBarrierKernel(int[] log)
    {
        using (var resource = new UsingRecorder { Log = log, Id = 1 })
        {
            Note(log, 10);
        }

        Gpu.SyncThreads();
        Note(log, 20);
    }
}
