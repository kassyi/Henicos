using System;
using Kassyi.GSharp.Core;

namespace ManagedStackEligibilityCases;

file struct IneligibleGroupValue
{
    public int V;
}

public ref struct CustomRefBuffer
{
    public int Value;
}

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class EligibilityKernels
{
    [GpuKernel]
    public static void BlockBarrierOnly(int[] data)
    {
        data[ThreadIndex.X] = 1;
        Gpu.SyncThreads();
        data[ThreadIndex.X] += 2;
    }

    [GpuKernel]
    public static void WithWarpShuffle(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        data[tid] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[tid], 1);
    }

    [GpuKernel]
    public static void WithActiveMask(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        data[tid] = (int)Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CallsActiveMaskReachingDeviceFunction(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid % 2 == 0)
        {
            data[tid] = ActiveMaskDeviceWait(data[tid]);
        }
    }

    // A using declaration after a continue that leaves the region of the loop body: UsingDeclarationScopes lowers it
    // to a using statement over the rest of its block, so the kernel runs on its stacks
    [GpuKernel]
    public static void ActiveMaskWithUsing(int[] data)
    {
        for (int i = 0; i < 2; i++)
        {
            if (ThreadIndex.X % 2 == 0)
            {
                continue;
            }

            using var guard = new Guard { V = i };
            data[ThreadIndex.X] += (int)Warp.ActiveMask() + guard.V;
        }
    }

    [GpuKernel]
    public static void CallsActiveMaskWithUsing(int[] data)
    {
        data[ThreadIndex.X] = (int)ActiveMaskWithUsingHelper(ThreadIndex.X);
    }

    [GpuDeviceFunction]
    public static uint ActiveMaskWithUsingHelper(int v)
    {
        using var guard = new Guard { V = v };
        return Warp.ActiveMask() + (uint)guard.V;
    }

    // A using declaration in a kernel that reaches warp collectives but not Warp.ActiveMask(): no regions, so the
    // kernel runs on its stacks
    [GpuKernel]
    public static void ShuffleWithUsing(int[] data)
    {
        using var guard = new Guard { V = ThreadIndex.X };
        data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, guard.V, 1);
    }

    public struct Guard : IDisposable
    {
        public int V;

        public void Dispose() => V = 0;
    }

    [GpuDeviceFunction]
    public static int ActiveMaskDeviceWait(int value)
    {
        if (value > 0)
        {
            value += (int)Warp.ActiveMask();
        }

        return value;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.SyncGrid();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void WithoutBarrier(int[] data)
    {
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void NonSpanRefStructParameter(int[] data, CustomRefBuffer custom)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = custom.Value;
    }

    [GpuKernel]
    public static void SpanParameterWithBarrier(Span<int> data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void CallsStackEligibleDeviceFunction(int[] data)
    {
        data[ThreadIndex.X] = StackDeviceWait(data[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void CallsShuffleReachingDeviceFunction(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = ShuffleDeviceWait(data[ThreadIndex.X]);
    }

    [GpuDeviceFunction]
    public static int StackDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return value + 1;
    }

    [GpuDeviceFunction]
    public static int ShuffleDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
    }

    [GpuKernel]
    public static void CallsStackExtension(int[] data)
    {
        data[ThreadIndex.X] = data.WaitAndAdd(1);
    }

    [GpuKernel]
    public static void PassesStackExtensionGroup(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, data.WaitAt);
    }

    [GpuDeviceFunction]
    public static int WaitAndAdd(this int[] data, int value)
    {
        Gpu.SyncThreads();
        return data[0] + value;
    }

    [GpuDeviceFunction]
    public static void WaitAt(this int[] data, int i)
    {
        Gpu.SyncThreads();
        data[i] += 1;
    }

    [GpuDeviceFunction]
    private static void IneligibleGroupStep(int i)
    {
        var value = new IneligibleGroupValue { V = i };
        Gpu.SyncThreads();
        _ = value.V;
    }

    [GpuKernel]
    public static void PassesIneligibleGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligibleGroupStep);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    public static void CallsIneligibleGroupHelper(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligibleGroupStep);
    }

    [GpuDeviceFunction]
    public static void IneligibleExtensionStep(this int[] data, int i)
    {
        var value = new IneligibleGroupValue { V = i };
        Gpu.SyncThreads();
        data[0] = value.V;
    }

    [GpuKernel]
    public static void PassesIneligibleExtensionGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, data.IneligibleExtensionStep);
    }

    [GpuKernel]
    public static void NameofIneligibleGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        var name = nameof(IneligibleGroupStep);
        data[ThreadIndex.X] = name.Length;
    }

    [GpuDeviceFunction]
    public static void PassesInstanceGroupHelper(int i)
    {
        Gpu.SyncThreads();
        var target = new InstanceEligibilityTarget();
        Gpu.ForUnroll(0, 2, target.InstanceGroupWait);
    }

    [GpuDeviceFunction]
    private static void SelfGroup(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, SelfGroup);
    }

    [GpuKernel]
    public static void PassesSelfGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, SelfGroup);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void CycleA(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, CycleB);
    }

    [GpuDeviceFunction]
    private static void CycleB(int i)
    {
        Gpu.SyncThreads();
        CycleA(i);
    }

    [GpuKernel]
    public static void PassesCycleKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, CycleA);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void EligibleGenericStep<T>(int i)
    {
        Gpu.SyncThreads();
        _ = i;
    }

    [GpuKernel]
    public static void PassesEligibleGenericGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, EligibleGenericStep<int>);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void IneligibleGenericStep<T>(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligibleGroupStep);
    }

    [GpuKernel]
    public static void PassesIneligibleGenericGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligibleGenericStep<int>);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static partial void EligiblePartialStep(int i);

    private static partial void EligiblePartialStep(int i)
    {
        Gpu.SyncThreads();
        _ = i;
    }

    [GpuKernel]
    public static void PassesEligiblePartialGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, EligiblePartialStep);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static partial void IneligiblePartialStep(int i);

    private static partial void IneligiblePartialStep(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligibleGroupStep);
    }

    [GpuKernel]
    public static void PassesIneligiblePartialGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, IneligiblePartialStep);
        data[ThreadIndex.X] += 1;
    }

    [GpuKernel]
    public static void StructCtorBarrierKernel(int[] data)
    {
        var h = new BarrierCtor(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void StructCtorAndDirectBarrierKernel(int[] data)
    {
        var h = new BarrierCtor(1);
        Gpu.SyncThreads();
        data[ThreadIndex.X] = h.Value;
    }

    [GpuDeviceFunction]
    public static int StructCtorDeviceWait(int value)
    {
        var h = new BarrierCtor(value);
        return h.Value;
    }

    [GpuKernel]
    public static void GenericStructBarrierKernel(int[] data)
    {
        var h = new GenericWrap<BarrierAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void GenericStructPlainKernel(int[] data)
    {
        var h = new GenericWrap<PlainAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void GridSyncStructCtorKernel(int[] data)
    {
        var h = new GridSyncCtor(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuDeviceFunction]
    public static int GridSyncStructCtorDeviceFunc(int value)
    {
        var h = new GridSyncCtor(value);
        return h.Value;
    }

    [GpuKernel]
    public static void CallsGridSyncStructCtorDeviceKernel(int[] data)
    {
        data[ThreadIndex.X] = GridSyncStructCtorDeviceFunc(1);
    }

    [GpuDeviceFunction]
    public static partial void PartialGenericStep<T>() where T : struct, IWrapAction;

    public static partial void PartialGenericStep<T>() where T : struct, IWrapAction => default(T).Run();

    [GpuKernel]
    public static void ConstrainedGenMethodKernel(int[] data)
    {
        var h = new GenericConstrainedGenMethodWrap<ImplWrapActionGen, BarrierAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void ConstrainedGenMethodPlainKernel(int[] data)
    {
        var h = new GenericConstrainedGenMethodWrap<ImplWrapActionGen, PlainAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void PartialGenericKernel(int[] data)
    {
        var h = new PartialGenericWrap<BarrierAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void PartialGenericPlainKernel(int[] data)
    {
        var h = new PartialGenericWrap<PlainAction>(1);
        data[ThreadIndex.X] = h.Value;
    }

    [GpuKernel]
    public static void PartialStructGenericDeviceFunctionKernel(int[] data)
    {
        PartialStructGenericHelper.PartialHelper<int>();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void PartialGenericStructDeviceFunctionKernel(int[] data)
    {
        PartialGenStruct<int>.PartialHelper();
        data[ThreadIndex.X] = 1;
    }

    public partial struct PartialStructGenericHelper
    {
        public static partial void PartialHelper<T>();

        [GpuDeviceFunction]
        public static partial void PartialHelper<T>()
        {
            Gpu.SyncThreads();
        }
    }

    public partial struct PartialGenStruct<T>
    {
        public static partial void PartialHelper();

        [GpuDeviceFunction]
        public static partial void PartialHelper()
        {
            Gpu.SyncThreads();
        }
    }

    public interface IWrapActionGen
    {
        void Run<U>() where U : struct, IWrapAction;
    }

    public struct ImplWrapActionGen : IWrapActionGen
    {
        public void Run<U>() where U : struct, IWrapAction => default(U).Run();
    }

    public struct GenericConstrainedGenMethodWrap<T, U> where T : struct, IWrapActionGen where U : struct, IWrapAction
    {
        public int Value;

        public GenericConstrainedGenMethodWrap(int x)
        {
            default(T).Run<U>();
            Value = x;
        }
    }

    public struct PartialGenericWrap<T> where T : struct, IWrapAction
    {
        public int Value;

        public PartialGenericWrap(int x)
        {
            PartialGenericStep<T>();
            Value = x;
        }
    }

    public interface IWrapAction
    {
        void Run();
    }

    public struct BarrierAction : IWrapAction
    {
        public void Run() => Gpu.SyncThreads();
    }

    public struct PlainAction : IWrapAction
    {
        public void Run()
        {
        }
    }

    public struct GenericWrap<T> where T : struct, IWrapAction
    {
        public int Value;

        public GenericWrap(int x)
        {
            default(T).Run();
            Value = x;
        }
    }

    public struct GridSyncCtor
    {
        public int Value;

        public GridSyncCtor(int value)
        {
            Gpu.SyncGrid();
            Value = value;
        }
    }

    public struct BarrierCtor
    {
        public int Value;

        public BarrierCtor(int value)
        {
            Value = StackDeviceWait(value);
        }
    }
}

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class UserDefinedNameofKernels
{
    private static void nameof(Action<int> step) => step(0);

    [GpuDeviceFunction]
    private static void IneligibleStep(int i)
    {
        var value = new IneligibleGroupValue { V = i };
        Gpu.SyncThreads();
        _ = value.V;
    }

    [GpuKernel]
    public static void CallsUserNameofWithIneligibleGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        nameof(IneligibleStep);
        data[ThreadIndex.X] += 1;
    }
}

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public partial class InstanceEligibilityTarget
{
    [GpuKernel]
    public void InstanceKernelWithBarrier(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void CallsInstanceDeviceFunction(int[] data)
    {
        Gpu.SyncThreads();
        var target = new InstanceEligibilityTarget();
        data[ThreadIndex.X] = target.InstanceDeviceWait(data[ThreadIndex.X]);
    }

    [GpuDeviceFunction]
    public int InstanceDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return value + 1;
    }

    [GpuDeviceFunction]
    public void InstanceGroupWait(int i)
    {
        Gpu.SyncThreads();
        _ = i;
    }
}

// Warp collectives on the managed stack model: which kernels run them on their stacks, and the limit of the visit
// counts of a GPU thread (WarpFrameLayout.MaxSize = 4096). The Chain functions double the counts of the one before:
// Chain0 takes 2, ChainN takes 1 + 2 * Chain(N - 1), so Chain10 takes 3071
[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class WarpEligibilityKernels
{
    [GpuKernel]
    public static void WarpOnly(int[] data)
    {
        data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
    }

    [GpuKernel]
    public static void CallsRecursiveShuffle(int[] data)
    {
        data[ThreadIndex.X] = RecursiveShuffle(data[ThreadIndex.X], 2);
    }

    [GpuDeviceFunction]
    public static int RecursiveShuffle(int value, int depth) =>
        depth == 0 ? Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1) : RecursiveShuffle(value, depth - 1);

    [GpuKernel]
    public static void PassesShuffleGroup(int[] data)
    {
        Gpu.SyncWarp();
        Gpu.ForUnroll(0, 2, data.ShuffleAt);
    }

    [GpuDeviceFunction]
    public static void ShuffleAt(this int[] data, int i)
    {
        data[i] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[i], 1);
    }

    // The kernel reaches Warp.ActiveMask() itself, so its stack form writes the body lowered for the stacks
    [GpuKernel]
    public static void PassesActiveMaskGroup(int[] data)
    {
        if (ThreadIndex.X % 2 == 0)
        {
            data[ThreadIndex.X] = (int)Warp.ActiveMask();
        }

        Gpu.ForUnroll(0, 2, data.ActiveMaskAt);
    }

    [GpuDeviceFunction]
    public static void ActiveMaskAt(this int[] data, int i)
    {
        data[i] += (int)Warp.ActiveMask();
    }

    // 1 + 1 + 3071 + 767 + 191 + 47 + 11 + 5 + 2 = 4096 visit counts
    [GpuKernel]
    public static void AtTheVisitLimit(int[] data)
    {
        int v = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
        data[ThreadIndex.X] = Chain10(v) + Chain8(v) + Chain6(v) + Chain4(v) + Chain2(v) + Chain1(v) + Chain0(v);
    }

    // One collective more: 4097 visit counts
    [GpuKernel]
    public static void OverTheVisitLimit(int[] data)
    {
        int v = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 2);
        data[ThreadIndex.X] = Chain10(v) + Chain8(v) + Chain6(v) + Chain4(v) + Chain2(v) + Chain1(v) + Chain0(v);
    }

    [GpuDeviceFunction]
    public static int Chain0(int v) => Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

    [GpuDeviceFunction]
    public static int Chain1(int v) => Chain0(v) + Chain0(v);

    [GpuDeviceFunction]
    public static int Chain2(int v) => Chain1(v) + Chain1(v);

    [GpuDeviceFunction]
    public static int Chain3(int v) => Chain2(v) + Chain2(v);

    [GpuDeviceFunction]
    public static int Chain4(int v) => Chain3(v) + Chain3(v);

    [GpuDeviceFunction]
    public static int Chain5(int v) => Chain4(v) + Chain4(v);

    [GpuDeviceFunction]
    public static int Chain6(int v) => Chain5(v) + Chain5(v);

    [GpuDeviceFunction]
    public static int Chain7(int v) => Chain6(v) + Chain6(v);

    [GpuDeviceFunction]
    public static int Chain8(int v) => Chain7(v) + Chain7(v);

    [GpuDeviceFunction]
    public static int Chain9(int v) => Chain8(v) + Chain8(v);

    [GpuDeviceFunction]
    public static int Chain10(int v) => Chain9(v) + Chain9(v);
}
