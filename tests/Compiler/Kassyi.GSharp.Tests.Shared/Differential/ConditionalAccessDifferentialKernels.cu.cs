#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessExtensionMethods;

namespace Kassyi.GSharp.Tests.Shared.Differential
{
    /// <summary>A struct read through a <c>Nullable&lt;T&gt;</c> by the null-conditional access.</summary>
    public struct ConditionalPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>A struct whose array field is read by a chained null-conditional access.</summary>
    public struct ConditionalHolder
    {
        public int[]? Data;
    }

    /// <summary>A struct whose array field of structs is read by a chained null-conditional access.</summary>
    public struct ConditionalPointsHolder
    {
        public ConditionalPoint[]? Pts;
    }

    /// <summary>
    /// A struct with a computed property and a nested struct field, read through a <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    public struct ConditionalComputed
    {
        public int X;

        public ConditionalPoint Inner;

        public int Twice => X * 2;
    }

    /// <summary>
    /// Differential kernels for the null-conditional access <c>?.</c> and <c>?[]</c> on an array and on a
    /// <c>Nullable&lt;T&gt;</c> of a struct: the length and an element of a null and of a non-null receiver, as a
    /// value, combined with <c>??</c>, chained, assigned through, next to and across a barrier.
    /// </summary>
    public static partial class ConditionalAccessDifferentialKernels
    {
        [GpuKernel]
        public static void ArrayConditionalAccessKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int[]? picked = a[t] > 0 ? null : a;
            int? length = picked?.Length;
            int? element = picked?[1];
            int fallback = picked?.Length ?? -1;

            int col = t * 5;
            dst[col + 0] = length.HasValue ? 1 : 0;
            dst[col + 1] = length ?? -7;
            dst[col + 2] = element.HasValue ? 1 : 0;
            dst[col + 3] = element ?? -9;
            dst[col + 4] = fallback;
        }

        [GpuKernel]
        public static void NullableStructConditionalAccessKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            ConditionalPoint? p = a[t] > 0 ? null : new ConditionalPoint { X = a[t], Y = 3 };
            int? x = p?.X;
            int? y = p?.Y;
            int sum = (p?.X ?? 100) + (p?.Y ?? 1000);

            int col = t * 5;
            dst[col + 0] = x.HasValue ? 1 : 0;
            dst[col + 1] = x ?? -5;
            dst[col + 2] = y.HasValue ? 1 : 0;
            dst[col + 3] = y ?? -6;
            dst[col + 4] = sum;
        }

        [GpuKernel]
        public static void ChainedConditionalAccessKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            // The holder is null for a quarter of the rows, its array for another quarter
            int sel = a[t] & 3;
            ConditionalHolder? holder = sel == 0 ? null : new ConditionalHolder { Data = sel == 1 ? null : a };
            int? length = holder?.Data?.Length;
            int? first = holder?.Data?[0];
            int viaCoalesce = holder?.Data?.Length ?? -3;

            int col = t * 5;
            dst[col + 0] = length.HasValue ? 1 : 0;
            dst[col + 1] = length ?? -4;
            dst[col + 2] = first.HasValue ? 1 : 0;
            dst[col + 3] = first ?? -8;
            dst[col + 4] = viaCoalesce;
        }

        [GpuDeviceFunction]
        public static ConditionalPoint? PickPoint(int selector, ref int calls)
        {
            calls++;
            return selector > 0 ? null : new ConditionalPoint { X = selector, Y = 4 };
        }

        [GpuKernel]
        public static void ReceiverEvaluatedOnceKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int calls = 0;
            int? x = PickPoint(a[t], ref calls)?.X;
            int? y = PickPoint(a[t], ref calls)?.Y;

            int col = t * 3;
            dst[col + 0] = calls;
            dst[col + 1] = x ?? -2;
            dst[col + 2] = y ?? -3;
        }

        [GpuDeviceFunction]
        public static int CountEvaluation(ref int evaluations)
        {
            evaluations++;
            return 42;
        }

        [GpuKernel]
        public static void ConditionalAssignmentKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int col = t * 4;
            dst[col + 0] = -1;
            dst[col + 3] = -1;
            int[]? target = a[t] > 0 ? null : dst;
            int evaluations = 0;

            // The right side is evaluated only when the target is not null
            target?[col] = CountEvaluation(ref evaluations);
            int? assigned = (target?[col + 3] = 7);

            dst[col + 1] = evaluations;
            dst[col + 2] = assigned ?? -9;
        }

        [GpuKernel]
        public static void StackArrayConditionalAccessKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            // A stack array is never null: the access always has a value
            int[] local = new int[4];
            local[2] = a[t];
            int? length = local?.Length;
            int? element = local?[2];

            int col = t * 3;
            dst[col + 0] = length ?? -1;
            dst[col + 1] = element ?? -2;
            dst[col + 2] = (local?[2] ?? 0) + 1;
        }

        [GpuDeviceFunction]
        public static int PlainEvaluation(ref int evaluations)
        {
            evaluations++;
            return 1;
        }

        [GpuDeviceFunction]
        public static int BarrierEvaluation(ref int evaluations)
        {
            Gpu.SyncThreads();
            evaluations++;
            return 1;
        }

        [GpuDeviceFunction]
        public static ConditionalPoint? BarrierPickPoint(int selector, ref int evaluations)
        {
            Gpu.SyncThreads();
            evaluations++;
            return selector == 0 ? null : new ConditionalPoint { X = 7, Y = 4 };
        }

        // The receiver itself suspends: it runs before the null test, for every row
        [GpuKernel]
        public static void SuspendingReceiverKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int e = 0;
            int? x = BarrierPickPoint(a[t] & 1, ref e)?.X;

            int col = t * 3;
            dst[col + 0] = e;
            dst[col + 1] = x.HasValue ? 1 : 0;
            dst[col + 2] = x ?? -1;
        }

        // The index is evaluated only for a non-null receiver, and a barrier follows the access
        [GpuKernel]
        public static void AccessBeforeBarrierKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? src = (a[t] & 1) == 0 ? null : a;
            int e = 0;
            int? v = src?[PlainEvaluation(ref e)];
            Gpu.SyncThreads();

            int col = t * 3;
            dst[col + 0] = e;
            dst[col + 1] = v.HasValue ? 1 : 0;
            dst[col + 2] = v ?? -1;
        }

        // An access in a loop that holds a barrier: the loop variable and the ref argument live in the frame
        [GpuKernel]
        public static void AccessInBarrierLoopKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? src = (a[t] & 1) == 0 ? null : a;
            int sum = 0;
            int e = 0;
            for (int i = 0; i < 2; i++)
            {
                int? v = src?[i + PlainEvaluation(ref e) - 1];
                Gpu.SyncThreads();
                sum += v ?? -1;
            }

            int col = t * 2;
            dst[col + 0] = e;
            dst[col + 1] = sum;
        }

        // A suspending call as an operand next to the access, after it and before it
        [GpuKernel]
        public static void SuspendingCallBesideAccessKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? src = (a[t] & 1) == 0 ? null : a;
            int e = 0;
            int g = 0;
            int r1 = (src?[PlainEvaluation(ref e)] ?? -1) + BarrierEvaluation(ref g);
            int r2 = BarrierEvaluation(ref g) + (src?[PlainEvaluation(ref e)] ?? -1);

            int col = t * 4;
            dst[col + 0] = e;
            dst[col + 1] = g;
            dst[col + 2] = r1;
            dst[col + 3] = r2;
        }

        // A compound assignment through ?[]: the right side runs only for a non-null receiver
        [GpuKernel]
        public static void CompoundAssignmentKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int k = t * 2;
            dst[k] = 5;
            int[]? target = (a[t] & 1) == 0 ? null : dst;
            int e = 0;
            target?[k] += PlainEvaluation(ref e) + 1;
            dst[k + 1] = e;
        }

        [GpuDeviceFunction]
        public static int Bump(int[] target, int index)
        {
            target[index] += 10;
            return 1;
        }

        [GpuDeviceFunction]
        public static int Combine(int x, int y) => x * 100 + y;

        // A read through ?[] and a write of the same element by a call, in both orders
        [GpuKernel]
        public static void ReadAndWriteOrderKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int k = t * 3;
            dst[k] = 5;
            int[]? src = (a[t] & 1) == 0 ? null : dst;
            int r1 = (src?[k] ?? -1) + Bump(dst, k);
            int r2 = Combine(Bump(dst, k), src?[k] ?? -1);
            dst[k + 1] = r1;
            dst[k + 2] = r2;
        }

        // A suspending call in one arm of ?: and the access in the other; mode is the same for every thread
        [GpuKernel]
        public static void SuspendingCallInOtherBranchKernel(int[] a, int[] dst, int mode)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? src = (a[t] & 1) == 0 ? null : a;
            int e = 0;
            int g = 0;
            int v = mode == 2 ? BarrierEvaluation(ref g) : (src?[PlainEvaluation(ref e)] ?? -1);

            int col = t * 3;
            dst[col + 0] = g;
            dst[col + 1] = e;
            dst[col + 2] = v;
        }

        // A computed property and a nested field of a Nullable<S> receiver
        [GpuKernel]
        public static void ComputedMemberKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            ConditionalComputed? p = (a[t] & 1) == 0
                ? null
                : new ConditionalComputed { X = 5, Inner = new ConditionalPoint { X = 1, Y = 9 } };
            int? twice = p?.Twice;
            int? inner = p?.Inner.Y;

            int col = t * 2;
            dst[col + 0] = twice ?? -1;
            dst[col + 1] = inner ?? -1;
        }

        // The array field of a struct parameter, as in the report of the missing diagnostic
        [GpuKernel]
        public static void HolderParameterKernel(ConditionalHolder h, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            dst[t] = h.Data?.Length ?? -1;
        }

        // An extension method called through ?.
        [GpuKernel]
        public static void ExtensionCallKernel(int[] a, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int[]? src = (a[t] & 1) == 0 ? null : a;
            int? s = src?.SumOfFirstTwo();

            int col = t * 2;
            dst[col + 0] = s.HasValue ? 1 : 0;
            dst[col + 1] = s ?? -1;
        }

        // A nested access whose receiver is an element: q?[t] is a Nullable<S>, read by the inner ?.
        [GpuKernel]
        public static void NestedElementReceiverKernel(int[] a, ConditionalPoint?[] nps, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            ConditionalPoint?[]? q = (a[t] & 1) == 0 ? null : nps;
            int? x = q?[t]?.X;

            int col = t * 2;
            dst[col + 0] = x.HasValue ? 1 : 0;
            dst[col + 1] = x ?? -1;
        }

        // ??= through ?[]: the right side runs only for a non-null receiver whose element is null
        [GpuKernel]
        public static void CoalesceAssignmentElementKernel(int[] a, int?[] nn, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int?[]? m = (a[t] & 1) == 0 ? null : nn;
            int evaluations = 0;
            m?[t] ??= CountEvaluation(ref evaluations);

            int col = t * 2;
            dst[col + 0] = nn[t] ?? -1;
            dst[col + 1] = evaluations;
        }

        // Extension calls on a binding of the access: an element by value and by ref this, a field of an element, a
        // nested field of a Nullable<S>; the extension class is in another namespace. The barrier makes the managed
        // path run the generated copy of the kernel
        [GpuKernel]
        public static void ExtensionOnBindingKernel(int[] a, ConditionalPoint[] ps, ConditionalComputed[] cs,
            int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            ConditionalPoint[]? q = (a[t] & 1) == 0 ? null : ps;
            int? plus = q?[t].PlusOne();
            q?[t].IncrementX();
            ConditionalComputed[]? c = (a[t] & 1) == 0 ? null : cs;
            int? deep = c?[t].Inner.PlusOne();
            ConditionalComputed? h = (a[t] & 2) == 0
                ? null
                : new ConditionalComputed { X = 1, Inner = new ConditionalPoint { X = a[t] & 0xFF, Y = 0 } };
            int? inner = h?.Inner.PlusOne();
            Gpu.SyncThreads();

            int col = t * 4;
            dst[col + 0] = plus ?? -1;
            dst[col + 1] = ps[t].X;
            dst[col + 2] = deep ?? -1;
            dst[col + 3] = inner ?? -1;
        }

        [GpuDeviceFunction]
        public static int Mark(ref int log, int digit, int value)
        {
            log = log * 10 + digit;
            return value;
        }

        // An inner access whose call is an extension method and whose receiver is a binding of the outer access
        // (.Data, [t]): the outer receiver once, then the inner receiver, then the call, each only when the one before
        // is not null
        [GpuKernel]
        public static void NestedExtensionCallKernel(int[] a, ConditionalPoint?[] nps, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int sel = a[t] & 3;
            ConditionalHolder? h = sel == 0 ? null : new ConditionalHolder { Data = sel == 1 ? null : a };
            int log1 = 0;
            int? v = (Mark(ref log1, 1, 0) == 0 ? h : null)?.Data?.SumOfFirstTwo();
            ConditionalPoint?[]? q = (a[t] & 1) == 0 ? null : nps;
            int log2 = 0;
            int? w = q?[Mark(ref log2, 2, t)]?.PlusOne();
            Gpu.SyncThreads();

            int col = t * 4;
            dst[col + 0] = v ?? -1;
            dst[col + 1] = log1;
            dst[col + 2] = w ?? -1;
            dst[col + 3] = log2;
        }

        // Extension methods that return void, called as statements: at the end of a chain (h?.Data?.M(),
        // hp?.Pts?[t].M()) and as the body of a Gpu.ForUnroll lambda, which is inlined as a statement
        [GpuKernel]
        public static void VoidExtensionStatementKernel(int[] a, int[] ws, ConditionalPoint[] ps1,
            ConditionalPoint[] ps2, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int sel = a[t] & 3;
            ConditionalHolder? h = sel == 0 ? null : new ConditionalHolder { Data = sel == 1 ? null : ws };
            ConditionalPointsHolder? hp = (a[t] & 4) == 0 ? null : new ConditionalPointsHolder { Pts = ps1 };
            ConditionalPoint[]? q = (a[t] & 1) == 0 ? null : ps2;
            Gpu.SyncThreads();
            h?.Data?.SetAt(t, 7);
            hp?.Pts?[t].IncrementX();
            Gpu.ForUnroll(0, 2, i => q?[t].IncrementX());
            Gpu.SyncThreads();

            int col = t * 3;
            dst[col + 0] = ws[t];
            dst[col + 1] = ps1[t].X;
            dst[col + 2] = ps2[t].X;
        }

        // The expression body of a void device function is a statement
        [GpuDeviceFunction]
        public static void IncrementElement(ConditionalPoint[]? q, int t) => q?[t].IncrementX();

        // The same with a division, which gives the function a managed version (written for the managed path)
        [GpuDeviceFunction]
        public static void AddQuotient(ConditionalPoint[]? q, int t, int a, int d) => q?[t].AddX(a / d);

        // Extension methods that return void as the expression body of a void device function (one with a managed
        // version), of a void local function and of a void lambda (Gpu.SingleThread, whose body thread 0 runs); the
        // barrier makes the managed path run the copy
        [GpuKernel]
        public static void VoidExtensionBodyKernel(int[] a, ConditionalPoint[] ps1, ConditionalPoint[] ps2,
            ConditionalPoint[] ps3, ConditionalPoint[] ps4, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            Gpu.SyncThreads();
            IncrementElement((a[t] & 1) == 0 ? null : ps1, t);
            AddQuotient((a[t] & 2) == 0 ? null : ps2, t, t * 10, (a[t] & 7) + 1);
            ConditionalPoint[]? q3 = (a[t] & 4) == 0 ? null : ps3;
            void IncrementOwn() => q3?[t].IncrementX();
            IncrementOwn();
            ConditionalPoint[]? q4 = (a[t] & 8) == 0 ? null : ps4;
            Gpu.SingleThread(() => q4?[t].IncrementX());
            Gpu.SyncThreads();

            int col = t * 4;
            dst[col + 0] = ps1[t].X;
            dst[col + 1] = ps2[t].X;
            dst[col + 2] = ps3[t].X;
            dst[col + 3] = ps4[t].X;
        }

        [GpuDeviceFunction]
        public static int SwapArray(ref int[]? r, int[] other)
        {
            r = other;
            return 0;
        }

        // The index reassigns the receiver variable: the access reads the receiver evaluated before the index
        [GpuKernel]
        public static void ReceiverReassignedKernel(int[] a, int[] b, int[] dst, int n)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            if (t >= n)
            {
                return;
            }

            int[]? r = (a[t] & 1) == 0 ? null : a;
            int? v = r?[SwapArray(ref r, b)];

            int col = t * 2;
            dst[col + 0] = v ?? -1;
            dst[col + 1] = r?[0] ?? -1;
        }
    }
}

// Internal, so that the generator snapshot, which compiles this file next to the test assembly, sees one copy; in a
// namespace of its own, so that the managed path has to find the extension methods of a call written in reduced form
namespace Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessExtensionMethods
{
    internal static partial class ConditionalAccessExtensions
    {
        [GpuDeviceFunction]
        public static int SumOfFirstTwo(this int[] values) => values[0] + values[1];

        [GpuDeviceFunction]
        public static int PlusOne(this ConditionalPoint p) => p.X + 1;

        [GpuDeviceFunction]
        public static void IncrementX(this ref ConditionalPoint p) => p.X++;

        [GpuDeviceFunction]
        public static void SetAt(this int[] values, int index, int value) => values[index] = value;

        [GpuDeviceFunction]
        public static void AddX(this ref ConditionalPoint p, int k) => p.X += k;
    }
}
