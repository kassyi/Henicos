using System;
using Kassyi.GSharp.Core;
using static SuspendingCallInStructMemberTest.SuspendingCallInStructMemberKernels;

namespace SuspendingCallInStructMemberTest
{
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

        [GpuDeviceFunction]
        public static void And(ref TruthHolder result, TruthHolder left, TruthHolder right)
        {
            result.Value = left.Value & right.Value;
        }

        [GpuDeviceFunction]
        public static void Or(ref TruthHolder result, TruthHolder left, TruthHolder right)
        {
            result.Value = left.Value | right.Value;
        }

        [GpuKernel]
        public static void ConstructorAndAccessorKernel(int[] output)
        {
            var h = new Holder(5);
            var scaled = new Holder(5, 2);
            var plain = new PlainHolder(3);
            h.Tag = 7;
            output[ThreadIndex.X] = h.Value + scaled.Value + plain.Value + h.Rotated;
        }

        [GpuKernel]
        public static void InitializerAndConversionKernel(int[] output)
        {
            var init = new InitializerHolder();
            ConversionHolder conv = new() { Value = 4 };
            int converted = conv;
            var rec = new RecordHolder(1);
            var prim = new PrimaryHolder(5);
            output[ThreadIndex.X] = init.Fld + init.Init + converted + rec.B + prim.V;
        }

        [GpuKernel]
        public static void AttributedMemberKernel(int[] output)
        {
            var h = new AttributedHolder { Value = 5 };
            h.Tag = 3;
            int converted = h;
            output[ThreadIndex.X] = h.Rotated + converted + AttributedHolder.Rotate(h.Value);
        }

        [GpuKernel]
        public static void DisposeAndTruthKernel(int[] output)
        {
            int x = 0;
            using (var d = new DisposeHolder { V = ThreadIndex.X })
            {
                x += d.V;
            }

            var a = new TruthHolder { Value = 2 };
            var b = new TruthHolder { Value = 1 };
            TruthHolder c = default;
            c = a && b;
            output[ThreadIndex.X] = x + c.Value;
        }

        // The same struct in a second kernel: still one report per call
        [GpuKernel]
        public static void SecondUserKernel(int[] output)
        {
            var h = new Holder(6);
            output[ThreadIndex.X] = h.Value;
        }

        [GpuKernel]
        public static void HostMemberKernel(int[] output)
        {
            var h = new HostMembers { V = 3 };
            output[ThreadIndex.X] = h.V + 1;
        }

        public static int HostUse()
        {
            var h = new HostMembers { V = 1 };
            using (h)
            {
            }

            return (h ? 1 : 0) + h.HostCompute() + (h + h).V + new UnusedStruct(1).V + new HostHolder(2).Value;
        }
    }

    public struct Holder
    {
        public int Value;

        public Holder(int tag)
        {
            Value = TaggedRotate(tag);
        }

        public Holder(int tag, int scale)
        {
            Func<int, int> rotate = t => TaggedRotate(t);
            Value = rotate(tag) * scale;
        }

        public int Rotated => TaggedRotate(Value);

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

        public PlainHolder(int value)
        {
            Value = NonSuspendingIncrement(value);
        }
    }

    public struct InitializerHolder
    {
        public int Fld = TaggedRotate(2);

        public int Init { get; init; } = TaggedRotate(1);

        public InitializerHolder() { }
    }

    public struct ConversionHolder
    {
        public int Value;

        public static implicit operator int(ConversionHolder h) => TaggedRotate(h.Value);
    }

    public record struct RecordHolder(int A)
    {
        public int B = TaggedRotate(3);
    }

    public struct PrimaryHolder(int t)
    {
        public int V = TaggedRotate(t);
    }

    public partial struct AttributedHolder
    {
        public int Value;

        public int Rotated
        {
            [GpuDeviceFunction]
            get => TaggedRotate(Value);
        }

        public int Tag
        {
            [GpuDeviceFunction]
            set => Value = TaggedRotate(value);
        }

        [GpuDeviceFunction]
        public static implicit operator int(AttributedHolder h) => TaggedRotate(h.Value);

        // A static device function of a struct is lowered like any other: not reported
        [GpuDeviceFunction]
        public static int Rotate(int tag) => TaggedRotate(tag);
    }

    public struct DisposeHolder : IDisposable
    {
        public int V;

        public void Dispose()
        {
            V = TaggedRotate(1);
        }
    }

    public struct TruthHolder
    {
        public int Value;

        [GpuOperatorMacro(typeof(SuspendingCallInStructMemberKernels), nameof(SuspendingCallInStructMemberKernels.And))]
        public static TruthHolder operator &(TruthHolder left, TruthHolder right) => throw new GpuOnlyException();

        [GpuOperatorMacro(typeof(SuspendingCallInStructMemberKernels), nameof(SuspendingCallInStructMemberKernels.Or))]
        public static TruthHolder operator |(TruthHolder left, TruthHolder right) => throw new GpuOnlyException();

        public static bool operator true(TruthHolder h) => TaggedRotate(h.Value) > 0;

        public static bool operator false(TruthHolder h) => TaggedRotate(h.Value) <= 0;
    }

    // Used by a kernel, but these members only by the host: none is written for the device, so none is reported
    public struct HostMembers : IDisposable
    {
        public int V;

        public static bool operator true(HostMembers h) => TaggedRotate(h.V) > 0;

        public static bool operator false(HostMembers h) => TaggedRotate(h.V) <= 0;

        public static HostMembers operator +(HostMembers a, HostMembers b) =>
            new() { V = TaggedRotate(a.V + b.V) };

        public int HostCompute() => TaggedRotate(V);

        public void Dispose()
        {
            V = TaggedRotate(1);
        }
    }

    // No device code uses it: not reported
    public struct UnusedStruct
    {
        public int V;

        public UnusedStruct(int t)
        {
            V = TaggedRotate(t);
        }
    }

    // A class is host code: not reported
    public sealed class HostHolder
    {
        public int Value;

        public HostHolder(int tag)
        {
            Value = TaggedRotate(tag);
        }
    }
}
