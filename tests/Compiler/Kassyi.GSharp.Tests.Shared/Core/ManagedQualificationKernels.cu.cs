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

    // A value whose addition is an operator macro: a device function that adds two has a managed version (#711)
    public struct QualifyWide
    {
        public int V;

        [GpuOperatorMacro(typeof(QualifyMath), nameof(QualifyMath.AddWide))]
        public static QualifyWide operator +(QualifyWide left, QualifyWide right) => throw new GpuOnlyException();
    }

    public static partial class QualifyMath
    {
        [GpuDeviceFunction]
        public static void AddWide(ref QualifyWide result, QualifyWide left, QualifyWide right) =>
            result.V = left.V + right.V;

        // 3x through the operator macro of QualifyWide
        [GpuDeviceFunction]
        public static int Thrice(int x)
        {
            QualifyWide once;
            once.V = x;
            QualifyWide twice;
            twice.V = x * 2;
            QualifyWide sum = default;
            sum = once + twice;
            return sum.V;
        }

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
    using Kassyi.ZeroAlloc.Linq;
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

        // A device function (Thrice) and constants (Offset, Count) named through using static inside the lambda of a
        // loop macro or a StackLinq query, and outside it. Thrice holds an operator macro, so these device functions have a
        // managed version (#711), whose body is the user's, not a lowered one: the names inside the lambda must be bound
        // on that body too, also in a lambda with an expression body, which the rewrite puts into a block.
        //
        // Each of the device-function / kernel pairs below has the same body on purpose: the kernel runs through the
        // lowered kernel body, the device function through its managed version. Edit both of a pair together.

        // Same body as GridStrideBlockBodyKernel, on the managed version (a block-bodied lambda)
        [GpuDeviceFunction]
        public static void ThriceEachStridedBlockBody(int[] values, int[] result, int count)
        {
            int outside = Thrice(Offset);
            Gpu.GridStrideLoop(count, i => { result[i] = Thrice(values[i]) + Offset + outside; });
        }

        // Same body as GridStrideExpressionBodyKernel, on the managed version (an expression-bodied lambda)
        [GpuDeviceFunction]
        public static void ThriceEachStridedExpressionBody(int[] values, int[] result, int count)
        {
            int outside = Thrice(Offset);
            Gpu.GridStrideLoop(count, i => result[i] = Thrice(values[i]) + Count + outside);
        }

        // Same body as ForUnrollKernel, on the managed version
        [GpuDeviceFunction]
        public static int SumThricesUnrolled(int value)
        {
            int sum = Thrice(Offset);
            Gpu.ForUnroll(0, Count, k => { sum += Thrice(value + k) + Offset; });
            return sum;
        }

        // Same body as StackLinqKernel, on the managed version
        [GpuDeviceFunction]
        public static int SumThricesAboveOffset(int[] values, int tid)
        {
            int outside = Thrice(Count);
            return values.AsSpan().AsStackEnumerable().Where(x => Thrice(x) > Offset).Select(x => x + Count).Sum() +
                   tid + outside;
        }

        // Same body as SingleThreadKernel, on the managed version
        [GpuDeviceFunction]
        public static void ThriceFirstOnce(int[] values, int[] result)
        {
            Gpu.SingleThread(() => result[0] = Thrice(values[0]) + Offset);
        }

        [GpuKernel]
        public static void GridStrideBlockBodyDeviceFunctionKernel(int[] values, int[] result, int count) =>
            ThriceEachStridedBlockBody(values, result, count);

        // Same body as ThriceEachStridedBlockBody, on the lowered kernel path
        [GpuKernel]
        public static void GridStrideBlockBodyKernel(int[] values, int[] result, int count)
        {
            int outside = Thrice(Offset);
            Gpu.GridStrideLoop(count, i => { result[i] = Thrice(values[i]) + Offset + outside; });
        }

        [GpuKernel]
        public static void GridStrideExpressionBodyDeviceFunctionKernel(int[] values, int[] result, int count) =>
            ThriceEachStridedExpressionBody(values, result, count);

        // Same body as ThriceEachStridedExpressionBody, on the lowered kernel path
        [GpuKernel]
        public static void GridStrideExpressionBodyKernel(int[] values, int[] result, int count)
        {
            int outside = Thrice(Offset);
            Gpu.GridStrideLoop(count, i => result[i] = Thrice(values[i]) + Count + outside);
        }

        [GpuKernel]
        public static void SingleThreadDeviceFunctionKernel(int[] values, int[] result) => ThriceFirstOnce(values, result);

        // Same body as ThriceFirstOnce, on the lowered kernel path
        [GpuKernel]
        public static void SingleThreadKernel(int[] values, int[] result)
        {
            Gpu.SingleThread(() => result[0] = Thrice(values[0]) + Offset);
        }

        [GpuKernel]
        public static void ForUnrollDeviceFunctionKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            result[tid] = SumThricesUnrolled(values[tid]);
        }

        // Same body as SumThricesUnrolled, on the lowered kernel path
        [GpuKernel]
        public static void ForUnrollKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            int sum = Thrice(Offset);
            Gpu.ForUnroll(0, Count, k => { sum += Thrice(values[tid] + k) + Offset; });
            result[tid] = sum;
        }

        [GpuKernel]
        public static void StackLinqDeviceFunctionKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            result[tid] = SumThricesAboveOffset(values, tid);
        }

        // Same body as SumThricesAboveOffset, on the lowered kernel path
        [GpuKernel]
        public static void StackLinqKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            int outside = Thrice(Count);
            result[tid] = values.AsSpan().AsStackEnumerable().Where(x => Thrice(x) > Offset).Select(x => x + Count).Sum() +
                          tid + outside;
        }
    }
}
