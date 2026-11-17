// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>An exception type of the test assembly, with a message constructor only.</summary>
public sealed class ThrowUserException : Exception
{
    public ThrowUserException(string message)
        : base(message)
    {
    }
}

/// <summary>Holds an exception type that is nested, so that its CLR name is <c>Holder+Nested</c>.</summary>
public static class ThrowHolder
{
    public sealed class NestedException : Exception
    {
        public NestedException(string message)
            : base(message)
        {
        }
    }
}

/// <summary>A generic exception type, so that its CLR name carries an arity and a type argument.</summary>
public sealed class ThrowGenericException<T> : Exception
{
    public ThrowGenericException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A resource that records its <c>Dispose</c>: <c>Id</c> when it starts and <c>-Id</c> when it ends, at
/// <c>Log[Log[0] + 1]</c>. It is declared here so that the file compiles alone, as the snapshot test compiles it.
/// </summary>
public struct ThrowResource : IDisposable
{
    public int[] Log;
    public int Id;

    public void Dispose()
    {
        Log[0] = Log[0] + 1;
        Log[Log[0]] = Id;
        Log[0] = Log[0] + 1;
        Log[Log[0]] = -Id;
    }
}

/// <summary>
/// Kernels that throw, one form each, for <see cref="ThrowTests"/>. Every kernel writes <c>outs[0]</c> before the throw
/// and <c>outs[1]</c> after it; the throw happens when <c>fail</c> is not 0. The plain C# method is the oracle.
/// </summary>
public static partial class ThrowKernels
{
    private const string Message = "const message";
    private const string Tail = $"{Message} and a tail";

    // Throws in a device function, as an expression of ?:
    [GpuDeviceFunction]
    public static int Checked(int v) => v >= 0 ? v : throw new ArgumentOutOfRangeException(nameof(v));

    // A device function whose body is a throw expression
    [GpuDeviceFunction]
    public static int Always(int v) => throw new InvalidOperationException("arrow body");

    // A device function that waits at a barrier, so that it may suspend, and throws after it
    [GpuDeviceFunction]
    public static int Suspending(int v)
    {
        Gpu.SyncThreads();
        if (v != 0)
        {
            throw new InvalidOperationException("after the barrier");
        }

        return 4;
    }

    // The arguments of the exception are evaluated before it is thrown, as C# does
    [GpuDeviceFunction]
    public static int Mark(int[] outs, int slot)
    {
        outs[slot] = 7;
        return slot;
    }

    [GpuKernel]
    public static void LiteralKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new InvalidOperationException("stop here");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void NoMessageKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ArgumentException();
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void NotImplementedKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new NotImplementedException();
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void FrameworkTypeKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new FormatException("bad format");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void UserTypeKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ThrowUserException("user type");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void NestedTypeKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ThrowHolder.NestedException("nested type");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void GenericTypeKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ThrowGenericException<int>("generic type");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ConstMessageKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new InvalidOperationException(Message);
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ConstInterpolatedKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new InvalidOperationException(Tail);
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ArgumentParamNameKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ArgumentException("bad", "p1");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ArgumentNullParamNameKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ArgumentNullException("p2");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ArgumentOutOfRangeParamNameKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ArgumentOutOfRangeException("p3", "msg3");
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void NameofParamNameKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fail));
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ArgumentsKernel(int[] outs, int fail)
    {
        if (fail != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fail), Mark(outs, 2), "fail is out of range");
        }

        outs[1] = 9;
    }

    [GpuKernel]
    public static void ConditionalInDeviceFunctionKernel(int[] outs, int fail)
    {
        outs[0] = Checked(fail != 0 ? -1 : 3);
        outs[1] = 9;
    }

    [GpuKernel]
    public static void ArrowBodyKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        if (fail != 0)
        {
            outs[1] = Always(fail);
        }

        outs[2] = 9;
    }

    [GpuKernel]
    public static void SwitchArmKernel(int[] outs, int fail)
    {
        outs[0] = fail switch
        {
            0 => 10,
            1 => throw new InvalidOperationException("switch arm"),
            _ => 20
        };
        outs[1] = 9;
    }

    [GpuKernel]
    public static void CoalesceValueKernel(int[] outs, int fail)
    {
        int? n = fail == 0 ? 4 : null;
        outs[0] = n ?? throw new InvalidOperationException("no value");
        outs[1] = 9;
    }

    [GpuKernel]
    public static void CoalesceReferenceKernel(int[] outs, int fail)
    {
        int[]? source = fail == 0 ? outs : null;
        outs[0] = (source ?? throw new InvalidOperationException("no array")).Length;
        outs[1] = 9;
    }

    [GpuKernel]
    public static void LoopKernel(int[] outs, int fail)
    {
        for (int i = 0; i < 5; i++)
        {
            outs[0] += 1;
            if (i == fail - 1)
            {
                throw new InvalidOperationException("in the loop");
            }
        }

        outs[1] = 9;
    }

    [GpuKernel]
    public static void SuspendingKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = Suspending(fail);
        outs[2] = 9;
    }

    // A throw with a literal message and a parameter name in a device function
    [GpuDeviceFunction]
    public static int ThrowsLiteral(int v)
    {
        if (v != 0)
        {
            throw new ArgumentException("device message", "dp");
        }

        return 6;
    }

    [GpuKernel]
    public static void DeviceLiteralKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = ThrowsLiteral(fail);
        outs[2] = 9;
    }

    // A user-defined exception type thrown in a device function
    [GpuDeviceFunction]
    public static int ThrowsUser(int v)
    {
        if (v != 0)
        {
            throw new ThrowUserException("device user type");
        }

        return 6;
    }

    [GpuKernel]
    public static void DeviceUserTypeKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = ThrowsUser(fail);
        outs[2] = 9;
    }

    // A throw two device function calls deep: the place is the throw, not a call
    [GpuDeviceFunction]
    public static int Inner(int v)
    {
        if (v != 0)
        {
            throw new InvalidOperationException("inner");
        }

        return 5;
    }

    [GpuDeviceFunction]
    public static int Middle(int v) => Inner(v) + 1;

    [GpuDeviceFunction]
    public static int Outer(int v) => Middle(v) + 1;

    [GpuKernel]
    public static void DeepKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = Outer(fail);
        outs[2] = 9;
    }

    // One device function with two throws, called from two kernels: each reports the throw it reached
    [GpuDeviceFunction]
    public static int Shared(int which)
    {
        if (which == 1)
        {
            throw new InvalidOperationException("shared one");
        }

        if (which == 2)
        {
            throw new InvalidOperationException("shared two");
        }

        return 3;
    }

    [GpuKernel]
    public static void SharedFirstKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = Shared(fail == 0 ? 0 : 1);
        outs[2] = 9;
    }

    [GpuKernel]
    public static void SharedSecondKernel(int[] outs, int fail)
    {
        outs[0] = 1;
        outs[1] = Shared(fail == 0 ? 0 : 2);
        outs[2] = 9;
    }

    // Thread 0 of the launch throws between its two writes; every other thread writes both
    [GpuKernel]
    public static void OtherThreadsKernel(int[] outs, int fail)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        outs[2 * t] = 1;
        if (t == 0 && fail != 0)
        {
            throw new InvalidOperationException("thread 0");
        }

        outs[2 * t + 1] = t + 10;
    }

    // Thread 0 throws before the block barrier; the other threads of its block pass it
    [GpuKernel]
    public static void BarrierKernel(int[] outs, int fail)
    {
        int t = ThreadIndex.X;
        outs[2 * t] = 1;
        if (t == 0 && fail != 0)
        {
            throw new InvalidOperationException("before the barrier");
        }

        Gpu.SyncThreads();
        outs[2 * t + 1] = t + 10;
    }

    // Lane 0 throws before the ballot; the lanes that go on take part in it without lane 0
    [GpuKernel]
    public static void BallotKernel(int[] outs, int fail)
    {
        int t = ThreadIndex.X;
        if (t == 0 && fail != 0)
        {
            throw new InvalidOperationException("lane 0");
        }

        uint lanes = Warp.BallotSync(0xFFFFFFFFu, true);
        outs[t] = (int)lanes;
    }

    // The body of a using throws: the resource is disposed once, as C# runs the finally block
    [GpuKernel]
    public static void UsingBodyKernel(int[] log, int[] outs, int fail)
    {
        using (var r = new ThrowResource { Log = log, Id = 1 })
        {
            outs[0] = 1;
            if (fail != 0)
            {
                throw new InvalidOperationException("in using");
            }
        }

        outs[1] = 2;
    }

    // Two nested usings: the inner is disposed first
    [GpuKernel]
    public static void UsingNestedKernel(int[] log, int[] outs, int fail)
    {
        using (var a = new ThrowResource { Log = log, Id = 1 })
        {
            using (var b = new ThrowResource { Log = log, Id = 2 })
            {
                outs[0] = 1;
                if (fail != 0)
                {
                    throw new InvalidOperationException("in nested using");
                }
            }

            outs[2] = 5;
        }

        outs[1] = 2;
    }

    // A using declaration in a loop: the pass that throws disposes its own resource, the earlier passes did before
    [GpuKernel]
    public static void UsingLoopKernel(int[] log, int[] outs, int fail)
    {
        for (int i = 0; i < 4; i++)
        {
            using var r = new ThrowResource { Log = log, Id = i + 1 };
            outs[0] += 1;
            if (i == fail - 1)
            {
                throw new InvalidOperationException("in using in a loop");
            }
        }

        outs[1] = 2;
    }

    // A throw in a device function that is called inside a using
    [GpuDeviceFunction]
    public static int Throwing(int fail)
    {
        if (fail != 0)
        {
            throw new InvalidOperationException("callee");
        }

        return 3;
    }

    [GpuKernel]
    public static void UsingCalleeKernel(int[] log, int[] outs, int fail)
    {
        using (var r = new ThrowResource { Log = log, Id = 1 })
        {
            outs[0] = Throwing(fail);
        }

        outs[1] = 2;
    }
}
