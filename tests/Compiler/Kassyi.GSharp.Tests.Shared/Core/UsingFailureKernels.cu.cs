// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

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

/// <summary>Holds a resource, so that <c>using (holder.Resource)</c> disposes a copy of it.</summary>
public struct FailingResourceHolder
{
    public FailingResource Resource;
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
    public static void SingleKernel(int[] log, int[] source, int[] outs, int bodyFail, int disposeFail)
    {
        using (var r = new FailingResource { Log = log, Source = source, Id = 1, Fail = disposeFail })
        {
            FailIf(bodyFail != 0, source);
            outs[0] += r.Id;
        }

        outs[1] = 7;
    }

    // Two nested using statements: a (id 1) outside b (id 2)
    [GpuKernel]
    public static void NestedKernel(int[] log, int[] source, int[] outs, int bodyFail, int failA, int failB)
    {
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
    public static void ReturnKernel(int[] log, int[] source, int[] outs, int x, bool early, int bodyFail, int failA,
        int failB)
    {
        outs[0] = Returns(log, source, x, early, bodyFail, failA, failB);
    }

    // A using declaration in a loop, left by continue and break; resource i + 1 fails its Dispose when i is
    // disposeFailAt
    [GpuKernel]
    public static void LoopKernel(int[] log, int[] source, int[] outs, int n, int skip, int stop, int failAt,
        int disposeFailAt)
    {
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
    public static void DeclaratorsKernel(int[] log, int[] source, int[] outs, int initFailB, int bodyFail, int failA,
        int failB)
    {
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
    public static void LabeledDeclarationKernel(int[] log, int[] source, int[] outs, int bodyFail, int disposeFail)
    {
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
    public static void SwitchKernel(int[] log, int[] source, int[] outs, int mode, int bodyFail, int disposeFail)
    {
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
    public static void NestedLoopKernel(int[] log, int[] source, int[] outs, int n, int stop, int disposeFailAt)
    {
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
    public static void GotoKernel(int[] log, int[] source, int[] outs, int mode, int disposeFail)
    {
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
    public static void RefReturnKernel(int[] log, int[] source, int[] outs, int index, int disposeFail)
    {
        PickRef(log, source, outs, index, disposeFail) = 42;
    }

    // a block lambda that returns a value out of its own using, called in the body of another using
    [GpuKernel]
    public static void LambdaKernel(int[] log, int[] source, int[] outs, int mode, int disposeFail)
    {
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
}
