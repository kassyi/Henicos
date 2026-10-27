using System;
using Kassyi.GSharp.Core;

// A namespace that the kernels below reach only through using directives. The managed fallback copies the kernel body
// into a generated file that has none of them, so every name from here must come out fully qualified there.
namespace Kassyi.GSharp.Tests.Shared.Core.Qualification
{
    public struct QualifyPair
    {
        public int A;
        public int B;
    }

    public struct QualifyBox<T> where T : unmanaged
    {
        public T Value;
    }

    public static class QualifyOuter
    {
        public struct Inner
        {
            public int X;
        }
    }

    public enum QualifyMode
    {
        Low = 1,
        High = 2
    }

    public static class QualifyConstants
    {
        public const int Offset = 5;
        public const int Count = 4;
    }

    public static partial class QualifyMath
    {
        [GpuDeviceFunction]
        public static int Twice(int x) => x * 2;

        [GpuDeviceFunction]
        public static int Triple(int x) => x * 3;

        // Reaches a barrier, so the managed fallback runs it as a state machine of its own
        [GpuDeviceFunction]
        public static int ExchangeWithNext(int[] buffer, int tid, int value)
        {
            QualifyPair pair = new QualifyPair { A = value, B = Twice(value) };
            buffer[tid] = pair.A + pair.B;
            Gpu.SyncThreads();
            int next = buffer[(tid + 1) % QualifyConstants.Count];
            Gpu.SyncThreads();
            return next;
        }
    }

    // Internal: the generator snapshot compiles this file against the test assembly, where public extension methods of
    // the same name would make the calls ambiguous (CS0121)
    internal static class QualifyExtensions
    {
        [GpuDeviceFunction]
        public static int Doubled(this int x) => x * 2;

        [GpuDeviceFunction]
        public static void Bump(ref this int x) => x += 1;

        [GpuDeviceFunction]
        public static int Total(in this QualifyPair pair) => pair.A + pair.B;
    }
}

namespace Kassyi.GSharp.Tests.Shared.Core
{
    using System.Runtime.CompilerServices;
    using Kassyi.GSharp.Tests.Shared.Core.Qualification;
    using static Kassyi.GSharp.Tests.Shared.Core.Qualification.QualifyConstants;
    using static Kassyi.GSharp.Tests.Shared.Core.Qualification.QualifyMath;
    using static Kassyi.GSharp.Tests.Shared.Core.Qualification.QualifyMode;
    using Q = Kassyi.GSharp.Tests.Shared.Core.Qualification;

    public static partial class ManagedQualificationKernels
    {
        // A type and a static class of another namespace, named through a using directive
        [GpuKernel]
        public static void UsingDirectiveKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            QualifyPair pair;
            pair.A = values[tid];
            pair.B = QualifyMath.Twice(values[tid]);
            result[tid] = pair.A + pair.B;
        }

        // A device function named without its class, through using static
        [GpuKernel]
        public static void UsingStaticKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            result[tid] = Triple(values[tid]) + 1;
        }

        // A local of a type from another namespace that lives across a barrier, so the managed state machine keeps it
        // in a field of its context class
        [GpuKernel]
        public static void HoistedLocalKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            QualifyPair pair = new QualifyPair { A = values[tid], B = Twice(values[tid]) };
            Gpu.SyncThreads();
            result[tid] = pair.A * 10 + pair.B;
        }

        // A ref struct local (declared again on every resume) whose size is a constant of another namespace, named
        // through using static, and which lives across a barrier
        [GpuKernel]
        public static void RefStructInitializerKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            SharedMemory<int> shared = new SharedMemory<int>(Count + Offset);
            shared[tid] = values[tid];
            Gpu.SyncThreads();
            result[tid] = shared[(tid + 1) % Count] + Offset;
        }

        // A generic type, a nested type, an enum and its members through using static (case, cast, default), a
        // stackalloc of a type of another namespace and a namespace alias
        [GpuKernel]
        public static void TypeFormsKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            QualifyBox<int> box;
            box.Value = values[tid];
            QualifyOuter.Inner inner;
            inner.X = Offset;
            QualifyMode mode = (QualifyMode)(tid % 2 + 1);
            int weight;
            switch (mode)
            {
                case Low:
                    weight = 1;
                    break;
                case High:
                    weight = 100;
                    break;
                default:
                    weight = 0;
                    break;
            }

            QualifyPair empty = default(QualifyPair);
            Span<QualifyPair> pairs = stackalloc QualifyPair[Count];
            pairs[tid].A = box.Value;
            result[tid] = pairs[tid].A * weight + inner.X + empty.A + Q.QualifyMath.Twice(1);
        }

        // Extension methods of another namespace, by value, ref this and in this, and an attribute inside the body
        [GpuKernel]
        public static void ExtensionCallKernel(int[] values, int[] result)
        {
            // Declared before its call: the native output does not hoist a local function (C3861 otherwise)
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int Next(int x) => x + 1;

            int tid = ThreadIndex.X;
            int bumped = values[tid];
            bumped.Bump();
            QualifyPair pair;
            pair.A = values[tid];
            pair.B = 1;
            result[tid] = Next(values[tid].Doubled()) + bumped * 100 + pair.Total() * 1000;
        }

        // A device function of another namespace that reaches barriers and names types of its namespace
        [GpuKernel]
        public static void SuspendingCalleeKernel(int[] buffer, int[] result)
        {
            int tid = ThreadIndex.X;
            result[tid] = ExchangeWithNext(buffer, tid, tid + 1);
        }
    }
}
