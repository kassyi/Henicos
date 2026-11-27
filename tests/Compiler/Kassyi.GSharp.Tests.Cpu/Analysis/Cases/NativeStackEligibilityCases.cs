using System;
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace NativeStackEligibilityCases
{
    public static class Gpu
    {
        public static void SyncThreads()
        {
        }
    }

    public static partial class Kernels
    {
        [GpuDeviceFunction]
        public static void SuspendingHelper(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void OuterSuspendingHelper(int[] data)
        {
            SuspendingHelper(data);
        }

        [GpuDeviceFunction]
        public static int ShuffleHelper(int x)
        {
            return Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1);
        }

        [GpuDeviceFunction]
        public static int OuterShuffleHelper(int x)
        {
            return ShuffleHelper(x);
        }

        [GpuDeviceFunction]
        public static void GridSyncHelper(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.SyncGrid();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void DirectlyRecursiveBarrier(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            Kassyi.GSharp.Core.Gpu.SyncThreads();
            DirectlyRecursiveBarrier(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void MutualPing(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            Kassyi.GSharp.Core.Gpu.SyncThreads();
            MutualPong(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void MutualPong(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            MutualPing(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void WrapperCallingRecursiveBarrier(int depth, int[] data)
        {
            DirectlyRecursiveBarrier(depth, data);
        }

        [Conditional("GSHARP_NEVER_DEFINED_CONDITION")]
        private static void OmittedBarrierCall()
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
        }

        [GpuKernel]
        public static void DirectBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void LocalFunctionBarrierKernel(int[] data)
        {
            void Step()
            {
                Kassyi.GSharp.Core.Gpu.SyncThreads();
            }

            Step();
            data[0] = 1;
        }

        [GpuKernel]
        public static void LambdaBarrierKernel(int[] data)
        {
            Action step = () => Kassyi.GSharp.Core.Gpu.SyncThreads();
            step();
            data[0] = 1;
        }

        [GpuKernel]
        public static void NoBarrierKernel(int[] data)
        {
            data[0] = 1;
        }

        [GpuKernel]
        public static void WarpShuffleAndBarrierKernel(int[] data)
        {
            int v = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[ThreadIndex.X] = v;
        }

        [GpuKernel]
        public static void WarpActiveMaskAndBarrierKernel(int[] data)
        {
            uint mask = Warp.ActiveMask();
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[ThreadIndex.X] = (int)mask;
        }

        [GpuKernel]
        public static void GridSyncAndBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.SyncGrid();
            data[0] = 1;
        }

        [GpuKernel(Cooperative = true)]
        public static void CooperativeBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void SuspendingDeviceFunctionKernel(int[] data)
        {
            SuspendingHelper(data);
        }

        [GpuKernel]
        public static void MultiLevelSuspendingKernel(int[] data)
        {
            OuterSuspendingHelper(data);
        }

        [GpuKernel]
        public static void ShuffleDeviceFunctionKernel(int[] data)
        {
            data[0] = ShuffleHelper(ThreadIndex.X);
        }

        [GpuKernel]
        public static void MultiLevelShuffleKernel(int[] data)
        {
            data[0] = OuterShuffleHelper(ThreadIndex.X);
        }

        [GpuKernel]
        public static void ShuffleOnlyKernel(int[] data)
        {
            data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
        }

        // A kernel called like a function has no frame in the visit counts of its caller
        [GpuKernel]
        public static void ShuffleCallingShuffleKernel(int[] data)
        {
            ShuffleOnlyKernel(data);
            data[0] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[0], 1);
        }

        // Only the state machine flattens a switch whose case values name no C++ label
        [GpuKernel]
        public static void FloatSwitchShuffleKernel(int[] data, float selector)
        {
            int value = ThreadIndex.X;
            switch (selector)
            {
                case 1f:
                    value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                    break;
                default:
                    value += 1;
                    break;
            }

            data[ThreadIndex.X] = value;
        }

        [GpuDeviceFunction]
        public static uint ActiveMaskHelper()
        {
            return Warp.ActiveMask();
        }

        [GpuKernel]
        public static void ActiveMaskDeviceFunctionKernel(int[] data)
        {
            data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, (int)ActiveMaskHelper(), 1);
        }

        [GpuKernel]
        public static void GridSyncDeviceFunctionKernel(int[] data)
        {
            GridSyncHelper(data);
        }

        public struct Guard : IDisposable
        {
            public int V;

            public void Dispose() => V = 0;
        }

        // A using declaration in a kernel that reaches Warp.ActiveMask(): UsingDeclarationScopes lowers it to a using
        // statement, so it runs on stacks
        [GpuKernel]
        public static void UsingDeclarationActiveMaskKernel(int[] data)
        {
            using var guard = new Guard { V = ThreadIndex.X };
            data[ThreadIndex.X] = (int)Warp.ActiveMask() + guard.V;
        }

        [GpuDeviceFunction]
        public static uint UsingDeclarationActiveMaskHelper(int v)
        {
            using var guard = new Guard { V = v };
            return Warp.ActiveMask() + (uint)guard.V;
        }

        // A using declaration in a device function that reaches Warp.ActiveMask(): lowered to a using statement, so its
        // caller runs on stacks
        [GpuKernel]
        public static void UsingDeclarationInActiveMaskCalleeKernel(int[] data)
        {
            data[ThreadIndex.X] = (int)UsingDeclarationActiveMaskHelper(ThreadIndex.X);
        }

        [GpuKernel]
        public static void UsingDeclarationShuffleKernel(int[] data)
        {
            using var guard = new Guard { V = ThreadIndex.X };
            data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, guard.V, 1);
        }

        [GpuKernel]
        public static void DirectRecursiveSuspendingKernel(int depth, int[] data)
        {
            DirectlyRecursiveBarrier(depth, data);
        }

        [GpuKernel]
        public static void MutuallyRecursiveSuspendingKernel(int depth, int[] data)
        {
            MutualPing(depth, data);
        }

        [GpuKernel]
        public static void WrapperCallingRecursiveSuspendingKernel(int depth, int[] data)
        {
            WrapperCallingRecursiveBarrier(depth, data);
        }

        [GpuKernel]
        public static void UserLookalikeBarrierKernel(int[] data)
        {
            Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void ConditionalOmittedCallKernel(int[] data)
        {
            OmittedBarrierCall();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void GridSyncStep(int i)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.SyncGrid();
        }

        [GpuDeviceFunction]
        public static void ShuffleStep(int i)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            _ = Warp.ShuffleXorSync(0xFFFFFFFFu, i, 1);
        }

        [GpuKernel]
        public static void MethodGroupGridSyncKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, GridSyncStep);
            data[0] = 1;
        }

        [GpuKernel]
        public static void MethodGroupRecursiveKernel(int depth, int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Action<int, int[]> step = DirectlyRecursiveBarrier;
            step(depth, data);
        }

        [GpuKernel]
        public static void MethodGroupWarpKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, ShuffleStep);
            data[0] = 1;
        }

        [GpuKernel]
        public static void MethodGroupActiveMaskKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Func<uint> step = ActiveMaskHelper;
            data[0] = (int)step();
        }

        [GpuKernel]
        public static void MethodGroupUsingActiveMaskKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Func<int, uint> step = UsingDeclarationActiveMaskHelper;
            data[0] = (int)step(ThreadIndex.X);
        }

        [GpuKernel]
        public static void MethodGroupNameofKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            var name = nameof(GridSyncStep);
            data[0] = name.Length;
        }

        [GpuDeviceFunction]
        public static void SelfGroup(int i)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, SelfGroup);
        }

        [GpuKernel]
        public static void MethodGroupSelfCycleKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, SelfGroup);
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void MutualGroupA(int i)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, MutualGroupB);
        }

        [GpuDeviceFunction]
        public static void MutualGroupB(int i)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            MutualGroupA(i);
        }

        [GpuKernel]
        public static void MethodGroupMutualCycleKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, MutualGroupA);
            data[0] = 1;
        }

        [GpuKernel]
        public static void StructCtorBarrierKernel(int[] data)
        {
            var h = new BarrierCtor(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void StructCtorAndDirectBarrierKernel(int[] data)
        {
            var h = new BarrierCtor(1);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = h.Value;
        }

        [GpuDeviceFunction]
        public static int StructCtorWrapper(int x)
        {
            var h = new BarrierCtor(x);
            return h.Value;
        }

        [GpuKernel]
        public static void WrapperCallingStructCtorKernel(int[] data)
        {
            data[0] = StructCtorWrapper(1);
        }

        [GpuKernel]
        public static void GenericStructBarrierKernel(int[] data)
        {
            var h = new GenericWrap<BarrierAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void GenericStructPlainKernel(int[] data)
        {
            var h = new GenericWrap<PlainAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void GridSyncStructCtorKernel(int[] data)
        {
            var h = new GridSyncCtor(1);
            data[0] = h.Value;
        }

        [GpuDeviceFunction]
        public static int GridSyncStructCtorDeviceFunc(int x)
        {
            var h = new GridSyncCtor(x);
            return h.Value;
        }

        [GpuKernel]
        public static void CallsGridSyncStructCtorDeviceKernel(int[] data)
        {
            data[0] = GridSyncStructCtorDeviceFunc(1);
        }

        [GpuDeviceFunction]
        public static partial void PartialGenericStep<T>() where T : struct, IWrapAction;

        public static partial void PartialGenericStep<T>() where T : struct, IWrapAction => default(T).Run();

        [GpuKernel]
        public static void ConstrainedGenMethodKernel(int[] data)
        {
            var h = new GenericConstrainedGenMethodWrap<ImplWrapActionGen, BarrierAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void ConstrainedGenMethodPlainKernel(int[] data)
        {
            var h = new GenericConstrainedGenMethodWrap<ImplWrapActionGen, PlainAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void PartialGenericKernel(int[] data)
        {
            var h = new PartialGenericWrap<BarrierAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void PartialGenericPlainKernel(int[] data)
        {
            var h = new PartialGenericWrap<PlainAction>(1);
            data[0] = h.Value;
        }

        [GpuKernel]
        public static void PartialStructGenericDeviceFunctionKernel(int[] data)
        {
            PartialStructGenericHelper.PartialHelper<int>();
            data[0] = 1;
        }

        [GpuKernel]
        public static void PartialGenericStructDeviceFunctionKernel(int[] data)
        {
            PartialGenStruct<int>.PartialHelper();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static partial void PartialPolyNonExtAttrDef<T>(int depth);

        public static partial void PartialPolyNonExtAttrDef<T>(int depth)
        {
            if (depth <= 0)
            {
                return;
            }

            PartialPolyNonExtAttrDef<PolyWrap<T>>(depth - 1);
        }

        [GpuKernel]
        public static void PartialPolyNonExtAttrDefKernel(int[] data)
        {
            PartialPolyNonExtAttrDef<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        public static partial void PartialPolyNonExtAttrImpl<T>(int depth);

        [GpuDeviceFunction]
        public static partial void PartialPolyNonExtAttrImpl<T>(int depth)
        {
            if (depth <= 0)
            {
                return;
            }

            PartialPolyNonExtAttrImpl<PolyWrap<T>>(depth - 1);
        }

        [GpuKernel]
        public static void PartialPolyNonExtAttrImplKernel(int[] data)
        {
            PartialPolyNonExtAttrImpl<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static partial int PartialPolyExtAttrDef<T>(this int depth, T value);

        public static partial int PartialPolyExtAttrDef<T>(this int depth, T value) =>
            depth <= 0 ? 0 : (depth - 1).PartialPolyExtAttrDef(new PolyWrap<T> { Value = value });

        [GpuKernel]
        public static void PartialPolyExtAttrDefKernel(int[] data)
        {
            data[0] = 2.PartialPolyExtAttrDef(0);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
        }

        public static partial int PartialPolyExtAttrImpl<T>(this int depth, T value);

        [GpuDeviceFunction]
        public static partial int PartialPolyExtAttrImpl<T>(this int depth, T value) =>
            depth <= 0 ? 0 : (depth - 1).PartialPolyExtAttrImpl(new PolyWrap<T> { Value = value });

        [GpuKernel]
        public static void PartialPolyExtAttrImplKernel(int[] data)
        {
            data[0] = 2.PartialPolyExtAttrImpl(0);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
        }

        [GpuDeviceFunction]
        public static partial void PartialPolyIndirectAttrDef<T>(int depth);

        public static partial void PartialPolyIndirectAttrDef<T>(int depth)
        {
            if (depth <= 0)
            {
                return;
            }

            PartialPolyIndirectHelperDef<T>(depth);
        }

        [GpuDeviceFunction]
        public static void PartialPolyIndirectHelperDef<U>(int depth) =>
            PartialPolyIndirectAttrDef<PolyWrap<U>>(depth - 1);

        [GpuKernel]
        public static void PartialPolyIndirectAttrDefKernel(int[] data)
        {
            PartialPolyIndirectAttrDef<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        public static partial void PartialPolyIndirectAttrImpl<T>(int depth);

        [GpuDeviceFunction]
        public static partial void PartialPolyIndirectAttrImpl<T>(int depth)
        {
            if (depth <= 0)
            {
                return;
            }

            PartialPolyIndirectHelperImpl<T>(depth);
        }

        [GpuDeviceFunction]
        public static void PartialPolyIndirectHelperImpl<U>(int depth) =>
            PartialPolyIndirectAttrImpl<PolyWrap<U>>(depth - 1);

        [GpuKernel]
        public static void PartialPolyIndirectAttrImplKernel(int[] data)
        {
            PartialPolyIndirectAttrImpl<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static partial void PartialPolyBarrierAttrDef<T>(int depth);

        public static partial void PartialPolyBarrierAttrDef<T>(int depth)
        {
            if (depth <= 0)
            {
                Kassyi.GSharp.Core.Gpu.SyncThreads();
                return;
            }

            PartialPolyBarrierAttrDef<PolyWrap<T>>(depth - 1);
        }

        [GpuKernel]
        public static void PartialPolyBarrierAttrDefKernel(int[] data)
        {
            PartialPolyBarrierAttrDef<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        public static partial void PartialPolyIndirectBarrierAttrImpl<T>(int depth);

        [GpuDeviceFunction]
        public static partial void PartialPolyIndirectBarrierAttrImpl<T>(int depth)
        {
            if (depth <= 0)
            {
                Kassyi.GSharp.Core.Gpu.SyncThreads();
                return;
            }

            PartialPolyIndirectBarrierHelperImpl<T>(depth);
        }

        [GpuDeviceFunction]
        public static void PartialPolyIndirectBarrierHelperImpl<U>(int depth) =>
            PartialPolyIndirectBarrierAttrImpl<PolyWrap<U>>(depth - 1);

        [GpuKernel]
        public static void PartialPolyIndirectBarrierAttrImplKernel(int[] data)
        {
            PartialPolyIndirectBarrierAttrImpl<int>(2);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }
    }

    public struct PolyWrap<T>
    {
        public T Value;
    }

    public partial struct PartialStructGenericHelper
    {
        public static partial void PartialHelper<T>();

        [GpuDeviceFunction]
        public static partial void PartialHelper<T>()
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
        }
    }

    public partial struct PartialGenStruct<T>
    {
        public static partial void PartialHelper();

        [GpuDeviceFunction]
        public static partial void PartialHelper()
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
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
            Kernels.PartialGenericStep<T>();
            Value = x;
        }
    }

    public interface IWrapAction
    {
        void Run();
    }

    public struct BarrierAction : IWrapAction
    {
        public void Run() => Kassyi.GSharp.Core.Gpu.SyncThreads();
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

        public GridSyncCtor(int x)
        {
            Kassyi.GSharp.Core.Gpu.SyncGrid();
            Value = x;
        }
    }

    public struct BarrierCtor
    {
        public int Value;

        public BarrierCtor(int x)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Value = x;
        }
    }

    public static partial class UsingStaticKernels
    {
        [GpuKernel]
        public static void UsingStaticBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }
    }
}

namespace NativeStackUsingStaticCases
{
    using static Kassyi.GSharp.Core.Gpu;

    public static partial class Kernels
    {
        [GpuKernel]
        public static void UsingStaticSyncThreadsKernel(int[] data)
        {
            SyncThreads();
            data[0] = 1;
        }
    }
}

namespace NativeStackOperatorMacroCases
{
    // An operator macro whose target reaches a warp collective: the expansion calls the target without the frame of the
    // stack model, which WarpFrameLayout gives only to the calls written as calls
    public struct WarpSum
    {
        public int V;

        [GpuOperatorMacro(typeof(WarpSumOps), nameof(WarpSumOps.Add))]
        public static WarpSum operator +(WarpSum left, WarpSum right) => throw new GpuOnlyException();

        [GpuOperatorMacro(typeof(WarpSumOps), "Subtract")]
        public static WarpSum operator -(WarpSum left, WarpSum right) => throw new GpuOnlyException();
    }

    public static partial class WarpSumOps
    {
        [GpuDeviceFunction]
        public static void Add(ref WarpSum result, WarpSum left, WarpSum right)
        {
            result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }

        // A target the kernel cannot name in C# (the managed path calls it through an accessor stub)
        [GpuDeviceFunction]
        private static void Subtract(ref WarpSum result, WarpSum left, WarpSum right)
        {
            result.V = left.V - Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }
    }

    public static partial class Kernels
    {
        [GpuKernel]
        public static void MacroAfterBarrierKernel(int[] data)
        {
            var x = new WarpSum { V = data[ThreadIndex.X] };
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            WarpSum z;
            z = x + x;
            data[ThreadIndex.X] = z.V;
        }

        [GpuKernel]
        public static void MacroAndShuffleKernel(int[] data)
        {
            var x = new WarpSum { V = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 2) };
            WarpSum z;
            z = x + x;
            data[ThreadIndex.X] = z.V;
        }

        [GpuKernel]
        public static void PrivateMacroTargetKernel(int[] data)
        {
            var x = new WarpSum { V = data[ThreadIndex.X] };
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            WarpSum z;
            z = x - x;
            data[ThreadIndex.X] = z.V;
        }
    }
}
