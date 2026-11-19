using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallInStructMemberKernels
{
    [GpuDeviceFunction]
    public static int TaggedRotate(int tag)
    {
        SharedMemory<int> shared = new(256);
        int tid = ThreadIndex.X;
        shared[tid] = tag + tid;
        Gpu.SyncThreads();
        return shared[tid ^ 1];
    }

    [GpuDeviceFunction]
    public static int NonSuspendingIncrement(int value) => value + 1;

    // Guard: a static [GpuDeviceFunction] calling TaggedRotate does not report GSH0062
    [GpuDeviceFunction]
    public static int SafeRotate(int tag) => TaggedRotate(tag);

    public struct Holder
    {
        public int Value;

        // GSH0062: suspending call in a struct constructor
        public Holder(int tag)
        {
            Value = TaggedRotate(tag);
        }

        // GSH0062: lambda inside a struct constructor reports GSH0062 once (as a lambda)
        public Holder(int tag, int scale)
        {
            Func<int, int> rotate = t => TaggedRotate(t);
            Value = rotate(tag) * scale;
        }

        // GSH0062: suspending call in a struct property getter
        public int Rotated => TaggedRotate(Value);

        // GSH0062: suspending call in a struct property setter
        public int Tag
        {
            set
            {
                Value = TaggedRotate(value);
            }
        }
    }

    public struct PlainHolder
    {
        public int Value;

        // Guard: non-suspending [GpuDeviceFunction] in a struct constructor does not report GSH0062
        public PlainHolder(int value)
        {
            Value = NonSuspendingIncrement(value);
        }
    }

    public struct InitializerHolder
    {
        // GSH0062: suspending call in a struct field initializer
        public int Fld = TaggedRotate(2);

        // GSH0062: suspending call in a struct property initializer
        public int Init { get; init; } = TaggedRotate(1);

        public InitializerHolder() { }
    }

    public struct ConversionHolder
    {
        public int Value;

        // GSH0062: suspending call in a struct user-defined conversion
        public static implicit operator int(ConversionHolder h) => TaggedRotate(h.Value);
    }

    public record struct RecordHolder(int A)
    {
        // GSH0062: suspending call in a record struct field initializer
        public int B = TaggedRotate(3);
    }

    public struct PrimaryHolder(int t)
    {
        // GSH0062: suspending call in a primary-constructor struct field initializer
        public int V = TaggedRotate(t);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ConstructorCallKernel(int[] output)
    {
        var h = new Holder(5);
        var scaled = new Holder(5, 2);
        var plain = new PlainHolder(3);
        output[ThreadIndex.X] = h.Value + scaled.Value + plain.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void AccessorCallKernel(int[] output)
    {
        var h = new Holder(5);
        h.Tag = 7;
        output[ThreadIndex.X] = h.Rotated;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void InitializerAndConversionKernel(int[] output)
    {
        var init = new InitializerHolder();
        ConversionHolder conv = new() { Value = 4 };
        int converted = conv;
        var rec = new RecordHolder(1);
        var prim = new PrimaryHolder(5);
        output[ThreadIndex.X] = init.Fld + init.Init + converted + rec.B + prim.V;
    }
}
