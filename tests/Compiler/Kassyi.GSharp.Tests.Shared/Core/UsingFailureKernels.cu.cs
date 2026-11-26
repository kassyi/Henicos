// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A resource that records its <c>Dispose</c>: <c>Id</c> when it starts and <c>-Id</c> when it ends, at
/// <c>Log[Log[0] + 1]</c>. <c>Fail</c> makes the <c>Dispose</c> fail between the two: 1 with
/// <see cref="ArgumentException"/> (a copy into a destination that is too short), 2 with
/// <see cref="ArgumentOutOfRangeException"/> (a span longer than the array).
/// </summary>
public struct FailingResource : IDisposable
{
    public int[] Log;
    public int[] Source;
    public int Id;
    public int Fail;

    public void Dispose()
    {
        Log[0] = Log[0] + 1;
        Log[Log[0]] = Id;
        if (Fail == 1)
        {
            Source.AsSpan().CopyTo(Log.AsSpan(0, 1));
        }

        if (Fail == 2)
        {
            Log[0] = Log.AsSpan(0, Log.Length + 1).Length;
        }

        Log[0] = Log[0] + 1;
        Log[Log[0]] = -Id;
    }
}

/// <summary>
/// A resource that counts its <c>Dispose</c> calls in <c>Counts[Slot]</c>, for the kernels of many threads.
/// </summary>
public struct CountingResource : IDisposable
{
    public int[] Counts;
    public int Slot;

    public void Dispose()
    {
        Counts[Slot] = Counts[Slot] + 1;
    }
}

/// <summary>Holds a resource, so that <c>using (holder.Resource)</c> disposes a copy of it.</summary>
public struct FailingResourceHolder
{
    public FailingResource Resource;
}

public struct UsingFailureBuffers
{
    public int[] Log;
    public int[] Source;
    public int[] Results;
}

public struct UsingReturnFailureOptions
{
    public int Value;
    public bool ReturnEarly;
    public int BodyFail;
    public int OuterDisposeFail;
    public int InnerDisposeFail;
}

public struct UsingLoopFailureOptions
{
    public int Iterations;
    public int SkipIndex;
    public int StopIndex;
    public int BodyFailIndex;
    public int DisposeFailIndex;
}

public struct UsingDeclaratorsFailureOptions
{
    public int SecondInitFail;
    public int BodyFail;
    public int FirstDisposeFail;
    public int SecondDisposeFail;
}

/// <summary>
/// Kernels in which the body of a <c>using</c>, its <c>Dispose</c> or both fail a C# check, for
/// <see cref="UsingFailureTests"/>: what is disposed, in which order, how often, and which failure the launch throws.
/// A failure of the body is <see cref="InvalidOperationException"/>, of an initializer
/// <see cref="ArgumentOutOfRangeException"/>. <c>outs[0]</c> is written in the body, <c>outs[1]</c> after the
/// <c>using</c>.
/// </summary>
public static partial class UsingFailureKernels
{
    // Fails with InvalidOperationException when fail is true
    [GpuDeviceFunction]
    public static void FailIf(bool fail, int[] source)
    {
        if (fail)
        {
            source[0] = ThreadStopKernels.FirstOf(source, 0);
        }
    }

    // Returns id; fails with ArgumentOutOfRangeException when initFail is not 0 (a span that starts past the end)
    [GpuDeviceFunction]
    public static int CheckedId(int[] source, int id, int initFail)
    {
        if (initFail != 0)
        {
            source[0] = source.AsSpan(initFail).Length;
        }

        return id;
    }

    // One using statement
    [GpuKernel]
    public static void SingleKernel(UsingFailureBuffers buffers, int bodyFail, int disposeFail)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = disposeFail })
        {
            FailIf(bodyFail != 0, source);
            outs[0] += r.Id;
        }

        outs[1] = 7;
    }

    // Two nested using statements: a (id 1) outside b (id 2)
    [GpuKernel]
    public static void NestedKernel(UsingFailureBuffers buffers, int bodyFail, int failA, int failB)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        using (var a = new FailingResource { Log = log, Source = source, Id = 1, Fail = failA })
        {
            using (var b = new FailingResource { Log = log, Source = source, Id = 2, Fail = failB })
            {
                FailIf(bodyFail != 0, source);
                outs[0] += 1;
            }

            outs[2] = 5;
        }

        outs[1] = 7;
    }

    // using declarations, an early return that leaves one using and a final return that leaves two
    [GpuDeviceFunction]
    public static int Returns(int[] log, int[] source, int x, bool early, int bodyFail, int failA, int failB)
    {
        using var a = new FailingResource { Log = log, Source = source, Id = 1, Fail = failA };
        if (early)
        {
            return x + 10;
        }

        using var b = new FailingResource { Log = log, Source = source, Id = 2, Fail = failB };
        FailIf(bodyFail != 0, source);
        return x + 20;
    }

    [GpuKernel]
    public static void ReturnKernel(UsingFailureBuffers buffers, UsingReturnFailureOptions options)
    {
        buffers.Results[0] = Returns(
            buffers.Log,
            buffers.Source,
            options.Value,
            options.ReturnEarly,
            options.BodyFail,
            options.OuterDisposeFail,
            options.InnerDisposeFail);
    }

    // A using declaration in a loop, left by continue and break; resource i + 1 fails its Dispose when i is
    // disposeFailAt
    [GpuKernel]
    public static void LoopKernel(UsingFailureBuffers buffers, UsingLoopFailureOptions options)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        int n = options.Iterations;
        int skip = options.SkipIndex;
        int stop = options.StopIndex;
        int failAt = options.BodyFailIndex;
        int disposeFailAt = options.DisposeFailIndex;
        for (int i = 0; i < n; i++)
        {
            int fail = i == disposeFailAt ? 1 : 0;
            using var r = new FailingResource { Log = log, Source = source, Id = i + 1, Fail = fail };
            if (i == skip)
            {
                continue;
            }

            if (i == stop)
            {
                break;
            }

            FailIf(i == failAt, source);
            outs[0] += i;
        }

        outs[1] = 7;
    }

    // Two declarators: the second initializer may fail after the first resource is declared
    [GpuKernel]
    public static void DeclaratorsKernel(UsingFailureBuffers buffers, UsingDeclaratorsFailureOptions options)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        int initFailB = options.SecondInitFail;
        int bodyFail = options.BodyFail;
        int failA = options.FirstDisposeFail;
        int failB = options.SecondDisposeFail;
        using (FailingResource a = new FailingResource { Log = log, Source = source, Id = 1, Fail = failA },
               b = new FailingResource
               {
                   Log = log, Source = source, Id = CheckedId(source, 2, initFailB), Fail = failB
               })
        {
            FailIf(bodyFail != 0, source);
            outs[0] += 1;
        }

        outs[1] = 7;
    }

    // using (expression) disposes a copy of the value, which a later change of its source does not reach
    [GpuKernel]
    public static void ExpressionKernel(int[] log, int[] source, int[] outs, int bodyFail)
    {
        var holder = new FailingResourceHolder
        {
            Resource = new FailingResource { Log = log, Source = source, Id = 4, Fail = 0 }
        };
        using (holder.Resource)
        {
            holder.Resource.Id = 99;
            FailIf(bodyFail != 0, source);
            outs[0] += 1;
        }

        outs[1] = 7;
    }

    // a using declaration under a label, reached by a forward goto
    [GpuKernel]
    public static void LabeledDeclarationKernel(UsingFailureBuffers buffers, int bodyFail, int disposeFail)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        int n = 0;
        if (bodyFail == 2)
        {
            goto again;
        }

        n = 10;
        again:
        using var r = new FailingResource { Log = log, Source = source, Id = n + 1, Fail = disposeFail };
        FailIf(bodyFail == 1, source);
        outs[0] += r.Id;
        outs[1] = 7;
    }

    // goto case / goto default / break out of a using in a switch section
    [GpuKernel]
    public static void SwitchKernel(UsingFailureBuffers buffers, int mode, int bodyFail, int disposeFail)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        switch (mode)
        {
            case 0:
                using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = disposeFail })
                {
                    FailIf(bodyFail != 0, source);
                    if (outs[1] == 0)
                    {
                        goto case 2;
                    }

                    outs[0] += 100;
                }

                break;
            case 1:
                using (var r = new FailingResource { Log = log, Source = source, Id = 2, Fail = disposeFail })
                {
                    outs[0] += 2;
                    goto default;
                }
            case 2:
                outs[0] += 20;
                break;
            case 3:
                using (var r = new FailingResource { Log = log, Source = source, Id = 3, Fail = disposeFail })
                {
                    outs[0] += 3;
                    break;
                }
            default:
                outs[0] += 50;
                break;
        }

        outs[1] = 7;
    }

    // continue (from a switch inside the body) and break leave two nested usings in a loop
    [GpuKernel]
    public static void NestedLoopKernel(UsingFailureBuffers buffers, int n, int stop, int disposeFailAt)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        for (int i = 0; i < n; i++)
        {
            using (var a = new FailingResource { Log = log, Source = source, Id = 10 + i, Fail = 0 })
            {
                int fail = i == disposeFailAt ? 1 : 0;
                using (var b = new FailingResource { Log = log, Source = source, Id = 20 + i, Fail = fail })
                {
                    switch (i)
                    {
                        case 1:
                            continue;
                        case 3:
                            break;
                    }

                    if (i == stop)
                    {
                        break;
                    }

                    outs[0] += i + 1;
                }

                outs[2] += 1;
            }
        }

        outs[1] = 7;
    }

    // gotos out of two usings, two of them to the same label
    [GpuKernel]
    public static void GotoKernel(UsingFailureBuffers buffers, int mode, int disposeFail)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        int pass = 0;
        retry:
        pass++;
        using (var a = new FailingResource { Log = log, Source = source, Id = pass, Fail = 0 })
        {
            using (var b = new FailingResource { Log = log, Source = source, Id = 10 + pass, Fail = disposeFail })
            {
                if (pass == 1)
                {
                    goto retry;
                }

                if (mode == 1)
                {
                    goto done;
                }

                outs[0] += 1;
            }

            if (mode == 3)
            {
                goto done;
            }
        }

        outs[0] += 100;
        done:
        outs[1] = 7;
    }

    // a by-ref return out of a using declaration
    [GpuDeviceFunction]
    public static ref int PickRef(int[] log, int[] source, int[] outs, int index, int disposeFail)
    {
        using var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = disposeFail };
        if (index == 0)
        {
            return ref outs[0];
        }

        return ref outs[r.Id];
    }

    [GpuKernel]
    public static void RefReturnKernel(UsingFailureBuffers buffers, int index, int disposeFail)
    {
        PickRef(buffers.Log, buffers.Source, buffers.Results, index, disposeFail) = 42;
    }

    // a block lambda that returns a value out of its own using, called in the body of another using
    [GpuKernel]
    public static void LambdaKernel(UsingFailureBuffers buffers, int mode, int disposeFail)
    {
        int[] log = buffers.Log;
        int[] source = buffers.Source;
        int[] outs = buffers.Results;
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = disposeFail })
        {
            Func<int, int> pick = v =>
            {
                using (var q = new FailingResource { Log = log, Source = source, Id = 5, Fail = 0 })
                {
                    if (v == 0)
                    {
                        return 9;
                    }
                }

                return v;
            };
            outs[0] = pick(mode);
            if (mode == 1)
            {
                return;
            }

            outs[0] += 1;
        }

        outs[1] = 7;
    }

    // A switch expression with no matching arm in the body of a using, written inline: the failure comes from the
    // kernel's own code and no callee, so it is the check that the CUDA build writes at the place of the expression
    [GpuKernel]
    public static void SwitchInBodyKernel(int[] log, int[] source, int[] outs, int mode)
    {
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 })
        {
#pragma warning disable CS8509
            outs[0] = mode switch
            {
                0 => 5,
                1 => 6,
            };
#pragma warning restore CS8509
            outs[1] = 9;
        }

        outs[2] = 3;
    }

    // The same failure under two using declarations and a using statement: each resource is disposed once, the last
    // declared first
    [GpuKernel]
    public static void SwitchUnderNestedKernel(int[] log, int[] source, int[] outs, int mode)
    {
        using var a = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 };
        using (var b = new FailingResource { Log = log, Source = source, Id = 2, Fail = 0 })
        {
            using var c = new FailingResource { Log = log, Source = source, Id = 3, Fail = 0 };
#pragma warning disable CS8509
            outs[0] = mode switch
            {
                0 => 5,
                1 => 6,
            };
#pragma warning restore CS8509
            outs[1] = 9;
        }

        outs[2] = 3;
    }

    // A failed switch expression after the using ended: nothing to dispose any more
    [GpuKernel]
    public static void SwitchAfterUsingKernel(int[] log, int[] source, int[] outs, int mode)
    {
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 })
        {
            outs[0] = 4;
        }

#pragma warning disable CS8509
        outs[1] = mode switch
        {
            0 => 5,
            1 => 6,
        };
#pragma warning restore CS8509
    }

    // A StackLinq terminal that fails in the body of a using: First and Average of an empty sequence, Max of a
    // sequence that Where leaves empty, First with a predicate that nothing matches. Mode 4 does not fail
    [GpuKernel]
    public static void LinqKernel(int[] log, int[] source, int[] outs, int mode)
    {
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 })
        {
            if (mode == 0)
            {
                outs[0] = source.AsSpan(0, 0).AsStackEnumerable().First();
            }

            if (mode == 1)
            {
                outs[0] = (int)source.AsSpan(0, 0).AsStackEnumerable().Average();
            }

            if (mode == 2)
            {
                outs[0] = source.AsSpan().AsStackEnumerable().Where(static x => x > 5).Max();
            }

            if (mode == 3)
            {
                outs[0] = source.AsSpan().AsStackEnumerable().First(static x => x > 5);
            }

            if (mode == 4)
            {
                outs[0] = source.AsSpan().AsStackEnumerable().First();
            }

            outs[1] = 9;
        }

        outs[2] = 3;
    }

    // A using declaration in a block that has ended, then a failing check under a using declaration after the block
    [GpuKernel]
    public static void BlockScopeKernel(int[] log, int[] source, int[] outs, int mode)
    {
        if (mode >= 0)
        {
            using var a = new FailingResource { Log = log, Source = source, Id = 1, Fail = 0 };
            outs[0] = 4;
        }

        using var b = new FailingResource { Log = log, Source = source, Id = 2, Fail = 0 };
#pragma warning disable CS8509
        outs[1] = mode switch
        {
            0 => 5,
            1 => 6,
        };
#pragma warning restore CS8509
        outs[2] = 3;
    }

    // Four threads; the odd ones fail a switch expression under two usings. counts[2 * t] and counts[2 * t + 1] count
    // the disposals of the resources of thread t
    [GpuKernel]
    public static void PartialKernel(int[] counts, int[] outs)
    {
        int t = ThreadIndex.X;
        using var a = new CountingResource { Counts = counts, Slot = 2 * t };
        using (var b = new CountingResource { Counts = counts, Slot = 2 * t + 1 })
        {
#pragma warning disable CS8509
            outs[t] = t % 2 == 1
                ? t switch
                {
                    0 => 1,
                    2 => 2,
                }
                : 10 + t;
#pragma warning restore CS8509
        }
    }
}
