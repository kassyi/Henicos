using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class StructMemberBarrierKernels
{
    [GpuDeviceFunction]
    public static int TaggedRotate(int tag, bool write)
    {
        var s = new SharedMemory<int>(32);
        int tid = ThreadIndex.X;
        if (write)
        {
            s[tid] = tid + tag;
        }

        Gpu.SyncThreads();
        return s[(tid + 1) % 32];
    }

    [GpuDeviceFunction]
    public static void SyncBlock() => Gpu.SyncThreads();

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void DirectInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = TaggedRotate(1, true);
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new BarrierHolder<int>(1, true);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PropertyInBranch(int[] o)
    {
        var h = new PlainRotated();
        if (ThreadIndex.X == 0)
        {
            o[0] = h.Rotated;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void SetterOnlyInBranch(int[] o)
    {
        var h = new SetterOnlyBarrier();
        if (ThreadIndex.X == 0)
        {
            h.Rotated = 1;
        }

        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GetterReachesSetterDoesNotAssignmentOnly(int[] o)
    {
        var h = new GetterOnlyBarrier();
        if (ThreadIndex.X == 0)
        {
            h.Rotated = 1;
        }

        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CompoundAssignmentGetterReaches(int[] o)
    {
        var h = new GetterOnlyBarrier();
        if (ThreadIndex.X == 0)
        {
            h.Rotated += 1;
        }

        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ChainedCtorInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new ChainedBarrierHolder(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void FieldInitializerInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new FieldInitBarrierHolder();
            o[0] = h.V;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void IndexerInBranch(int[] o)
    {
        var h = new IndexerBarrierHolder();
        if (ThreadIndex.X == 0)
        {
            o[0] = h[0];
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ImplicitConversionInBranch(int[] o)
    {
        var h = new ConversionBarrierHolder { X = 1 };
        if (ThreadIndex.X == 0)
        {
            int v = h;
            o[0] = v;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformStructForms(int[] o)
    {
        var h = new BarrierHolder<int>(1, true);
        var p = new PlainRotated();
        var conv = new ConversionBarrierHolder { X = h.Value };
        int v = conv;
        o[0] = p.Rotated + v;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ThreadVariantCtorArg(int[] o)
    {
        var h = new BranchingBarrierHolder(ThreadIndex.X);
        o[0] = h.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CollectionBuilderInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            StructBarrierBag bag = [1, 2];
            o[0] = bag.First;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ImplicitDefaultCtorFieldInitInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new FieldInitWithParamCtorHolder();
            o[0] = h.V;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ParamCtorFieldInitInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new FieldInitWithParamCtorHolder(5);
            o[0] = h.V;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UsingVarDisposeInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            using var d = new BarrierDisposable { X = 1 };
            o[0] = d.X;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedBlockUsingVarDisposeNoError(int[] o)
    {
        {
            using var d = new BarrierDisposable { X = 1 };
            o[0] = d.X;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UsingStatementDisposeInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            using (var d = new BarrierDisposable { X = 1 })
            {
                o[0] = d.X;
            }
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static unsafe void FixedGetPinnableInBranch(int[] o)
    {
        var p = new PinnableBarrierHolder { Data = o };
        if (ThreadIndex.X == 0)
        {
            fixed (int* ptr = p)
            {
                *ptr = 1;
            }
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void TruthOperatorInBranch(int[] o)
    {
        var a = new TruthBarrierHolder { X = 1 };
        var b = new TruthBarrierHolder { X = 2 };
        if (ThreadIndex.X == 0)
        {
            var c = a && b;
            o[0] = c.X;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UserDefinedBinaryOperatorInBranch(int[] o)
    {
        var a = new OperatorBarrierHolder { X = 1 };
        var b = new OperatorBarrierHolder { X = 2 };
        if (ThreadIndex.X == 0)
        {
            var c = a + b;
            o[0] = c.X;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedTupleDeconstructGetterOnlyInBranch(int[] o)
    {
        var h = new GetterOnlyBarrier();
        int a;
        int b;
        if (ThreadIndex.X == 0)
        {
            ((h.Rotated, a), b) = ((1, 2), 3);
            o[0] = a + b;
        }

        o[1] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedTupleDeconstructSetterOnlyInBranch(int[] o)
    {
        var h = new SetterOnlyBarrier();
        int a;
        int b;
        if (ThreadIndex.X == 0)
        {
            ((h.Rotated, a), b) = ((1, 2), 3);
            o[0] = a + b;
        }

        o[1] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedTupleDeconstructVariantSetter(int[] o)
    {
        var h = new BranchingSetterBarrier();
        int a;
        int b;
        ((h.Rotated, a), b) = ((ThreadIndex.X, 1), 2);
        o[0] = h.X + a + b;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CompoundAssignUniformGetterExitOnlySetterLast(int[] o)
    {
        var h = new UniformGetterExitOnlySetterBarrier();
        h.Rotated += ThreadIndex.X;
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void InvocationUniformPlusExitOnlyConversionLast(int[] o)
    {
        int v = GetUniformBarrierConvHolder(ThreadIndex.X);
        o[0] = v;
    }

    [GpuDeviceFunction]
    public static ExitOnlyConversionHolder GetUniformBarrierConvHolder(int x)
    {
        Gpu.SyncThreads();
        return new ExitOnlyConversionHolder { X = x };
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorArgBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new BarrierHolder<int>(s[0], true);
        o[0] = h.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CollectionBuilderArgBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        StructBarrierBag bag = [s[0]];
        o[0] = bag.First;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorOutUniformOverwritesVariantBeforeInitializer(int[] o)
    {
        int y = ThreadIndex.X;
        var h = new OutUniformBarrierCtor(out y) { Prop = y };
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorOutVariantBeforeInitializer(int[] o)
    {
        int y = 0;
        var h = new OutVariantBarrierCtor(out y) { Prop = y };
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignGetterInBranch(int[] o)
    {
        var h = new NullableGetterBarrier();
        if (ThreadIndex.X == 0)
        {
            h.Prop ??= 0;
        }

        Gpu.SyncThreads();
        o[0] = h.Value ?? 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignSetterInBranch(int[] o)
    {
        var h = new NullableSetterBarrier();
        if (ThreadIndex.X == 0)
        {
            h.Prop ??= 0;
        }

        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignSetterVariantGet(int[] o)
    {
        var h = new NullableVariantGetterSetterBarrier();
        h.Prop ??= 0;
        Gpu.SyncThreads();
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignSetterVariantRhs(int[] o)
    {
        var h = new NullableSetterBarrier();
        h.Prop ??= ThreadIndex.X;
        Gpu.SyncThreads();
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignIndexerSetterVariantRhs(int[] o)
    {
        var h = new NullableIndexerSetterBarrier();
        h[0] ??= ThreadIndex.X;
        Gpu.SyncThreads();
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CoalesceAssignRhsBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new NullableSetterBarrier();
        h.Prop ??= s[0];
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ParenthesizedUsingStatementDisposeInBranch(int[] o)
    {
        var d = new BarrierDisposable { X = 1 };
        if (ThreadIndex.X == 0)
        {
            using ((d))
            {
                o[0] = d.X;
            }
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorWithInitArgBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new BarrierHolder<int>(s[0], true) { Value = 1 };
        o[0] = h.Value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CtorWithInitReadAfterSplitSpanLive(int[] o)
    {
        Span<int> s = o;
        var h = new BarrierHolder<int>(1, true) { Value = 1 };
        o[0] = h.Value + s[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PropSetterRhsBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new SetterOnlyBarrier();
        h.Rotated = s[0];
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void IndexerSetterRhsBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new IndexerSetterOnlyBarrier();
        h[0] = s[0];
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CompoundAssignRhsBeforeSplitSpanNotLive(int[] o)
    {
        Span<int> s = o;
        var h = new SetterOnlyBarrier { X = 1 };
        h.Rotated += s[0];
        o[0] = h.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PropSetterRhsThenReadAfterSplitSpanLive(int[] o)
    {
        Span<int> s = o;
        var h = new SetterOnlyBarrier();
        h.Rotated = s[0];
        o[0] = h.X + s[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CompoundAssignInConversionVariantTarget(int[] o)
    {
        int acc = ThreadIndex.X;
        acc += new InConvBox { X = 1 };
        o[0] = acc;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void CompoundAssignInConversionUniformTargetVariantValue(int[] o)
    {
        int acc = 0;
        int tid = ThreadIndex.X;
        acc += new InConvBox { X = tid };
        o[0] = acc;
    }

    [GpuDeviceFunction]
    public static int SyncBlockInt()
    {
        Gpu.SyncThreads();
        return 1;
    }

    [GpuDeviceFunction]
    public static void GenericActionCaller<T>() where T : struct, IBarrierAction => default(T).Run();

    [GpuDeviceFunction]
    public static void SetInterfaceProp<T>(ref T p, int v) where T : struct, IExplicitProp => p.Prop = v;

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GenericConstrainedInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericConstrainedHolder<BarrierAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GenericNewTInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericNewTHolder<CtorBarrierAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GenericUsingTInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericUsingTHolder<DisposableBarrierAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedGenericOuterInnerInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericOuter<BarrierAction>.Inner(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void NestedGenericBothInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericOuter<PlainAction>.Nested<BarrierAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GenericDeviceFuncOrderInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            GenericActionCaller<PlainAction>();
            GenericActionCaller<BarrierAction>();
            o[0] = 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void GenericBoxPropInBranch(int[] o)
    {
        var b = new GenericPropBox<BarrierAction>();
        if (ThreadIndex.X == 0)
        {
            o[0] = b.Prop;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ExplicitInterfacePropSetterInBranch(int[] o)
    {
        var p = new ExplicitPropBarrier();
        if (ThreadIndex.X == 0)
        {
            SetInterfaceProp(ref p, 0);
        }

        o[0] = p.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PrimaryCtorBranchingSingleFieldVariant(int[] o)
    {
        var c = new PrimaryBranchingCtor(ThreadIndex.X);
        Gpu.SyncThreads();
        o[0] = c.V;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PrimaryCtorBranchingMultiFieldVariant(int[] o)
    {
        var c = new PrimaryMultiFieldCtor(ThreadIndex.X);
        o[0] = c.V + c.After;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PrimaryCtorBranchingUniformArg(int[] o)
    {
        var c = new PrimaryMultiFieldCtor(1);
        o[0] = c.V + c.After;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void RecordPrimaryCtorBranchingVariant(int[] o)
    {
        var c = new RecordBranchingCtor(ThreadIndex.X);
        Gpu.SyncThreads();
        o[0] = c.V;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void RecordPrimaryCtorBranchingUniformArg(int[] o)
    {
        var c = new RecordBranchingCtor(1);
        Gpu.SyncThreads();
        o[0] = c.V;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void RecordPrimaryCtorPositionalVariance(int[] o)
    {
        var r = new PositionalRecord(ThreadIndex.X);
        if (r.X > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        o[0] = r.Y;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void RecordPrimaryCtorPositionalUniform(int[] o)
    {
        var r = new PositionalRecord(1);
        if (r.X > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        o[0] = r.Y;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PrimaryCtorChainedVariant(int[] o)
    {
        var c = new ChainedPrimary(ThreadIndex.X, 1);
        Gpu.SyncThreads();
        o[0] = c.V;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ExplicitCtorWithBranchingFieldInitVariant(int[] o)
    {
        var c = new ExplicitCtorBranchingFieldInit(1);
        Gpu.SyncThreads();
        o[0] = c.V;
    }

    [GpuDeviceFunction]
    public static partial void PartialGeneric<T>() where T : struct, IBarrierAction;

    public static partial void PartialGeneric<T>() where T : struct, IBarrierAction => default(T).Run();

    public static partial void PartialGenericAttrOnImpl<T>() where T : struct, IBarrierAction;

    [GpuDeviceFunction]
    public static partial void PartialGenericAttrOnImpl<T>() where T : struct, IBarrierAction => default(T).Run();

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ConstrainedGenMethodInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericConstrainedGenMethodHolder<ImplActionGen, BarrierAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void ConstrainedGenMethodPlainInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            var h = new GenericConstrainedGenMethodHolder<ImplActionGen, PlainAction>(1);
            o[0] = h.Value;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PartialGenericInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            PartialGeneric<BarrierAction>();
            o[0] = 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PartialGenericPlainInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            PartialGeneric<PlainAction>();
            o[0] = 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void PartialGenericAttrOnImplInBranch(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            PartialGenericAttrOnImpl<BarrierAction>();
            o[0] = 1;
        }
    }

    [GpuDeviceFunction]
    public static partial int PartialIdentityAttrOnDef(int x);

    public static partial int PartialIdentityAttrOnDef(int x) => x;

    public static partial int PartialIdentityAttrOnImpl(int x);

    [GpuDeviceFunction]
    public static partial int PartialIdentityAttrOnImpl(int x) => x;

    [GpuDeviceFunction]
    public static partial int PartialCondSyncAttrOnDef(int x);

    public static partial int PartialCondSyncAttrOnDef(int x)
    {
        if (x > 0)
        {
            Gpu.SyncThreads();
        }

        return x;
    }

    public static partial int PartialCondSyncAttrOnImpl(int x);

    [GpuDeviceFunction]
    public static partial int PartialCondSyncAttrOnImpl(int x)
    {
        if (x > 0)
        {
            Gpu.SyncThreads();
        }

        return x;
    }

    [GpuDeviceFunction]
    public static partial int PartialCondTwoBarriersAttrOnDef(int x);

    public static partial int PartialCondTwoBarriersAttrOnDef(int x)
    {
        if (x > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        return x;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformPartialIdentityDef(int[] o)
    {
        int y = PartialIdentityAttrOnDef(1);
        if (y == 0)
        {
            Gpu.SyncThreads();
        }

        o[0] = y;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformPartialIdentityImpl(int[] o)
    {
        int y = PartialIdentityAttrOnImpl(1);
        if (y == 0)
        {
            Gpu.SyncThreads();
        }

        o[0] = y;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformPartialCondSyncDefFollowedByBarrier(int[] o)
    {
        o[0] = PartialCondSyncAttrOnDef(1);
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformPartialCondSyncImplFollowedByBarrier(int[] o)
    {
        o[0] = PartialCondSyncAttrOnImpl(1);
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void VariantPartialTwoBarriersDef(int[] o)
    {
        o[0] = PartialCondTwoBarriersAttrOnDef(ThreadIndex.X);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Input for BarrierUsageAnalyzerTests")]
    public static void UniformPartialIndexer(int[] o)
    {
        var h = new PartialIndexerHolder { X = 1 };
        int y = h[1];
        if (y == 0)
        {
            Gpu.SyncThreads();
        }

        o[0] = y;
    }
}

public struct BarrierHolder<T> where T : unmanaged
{
    public int Value;

    public BarrierHolder(int tag, bool write)
    {
        Value = StructMemberBarrierKernels.TaggedRotate(tag, write);
    }
}

public struct PlainRotated
{
    public int X;

    public int Rotated => StructMemberBarrierKernels.TaggedRotate(X, true);
}

public struct SetterOnlyBarrier
{
    public int X;

    public int Rotated
    {
        get => X;
        set => X = StructMemberBarrierKernels.TaggedRotate(value, true);
    }
}

public struct GetterOnlyBarrier
{
    public int X;

    public int Rotated
    {
        get => StructMemberBarrierKernels.TaggedRotate(X, true);
        set => X = value;
    }
}

public struct ChainedBarrierHolder
{
    public int Value;

    public ChainedBarrierHolder(int tag) : this(tag, true)
    {
    }

    public ChainedBarrierHolder(int tag, bool write)
    {
        Value = StructMemberBarrierKernels.TaggedRotate(tag, write);
    }
}

public struct FieldInitBarrierHolder
{
    public int V = StructMemberBarrierKernels.TaggedRotate(1, true);

    public FieldInitBarrierHolder()
    {
    }
}

public struct IndexerBarrierHolder
{
    public int X;

    public int this[int index] => StructMemberBarrierKernels.TaggedRotate(X + index, true);
}

public struct ConversionBarrierHolder
{
    public int X;

    public static implicit operator int(ConversionBarrierHolder holder) =>
        StructMemberBarrierKernels.TaggedRotate(holder.X, true);
}

public struct BranchingBarrierHolder
{
    public int Value;

    public BranchingBarrierHolder(int x)
    {
        if (x > 0)
        {
            Value = StructMemberBarrierKernels.TaggedRotate(x, true);
        }
        else
        {
            Value = 0;
        }

        StructMemberBarrierKernels.SyncBlock();
    }
}

[CollectionBuilder(typeof(StructBarrierBagBuilder), nameof(StructBarrierBagBuilder.Create))]
public struct StructBarrierBag
{
    public int First;

    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

public static partial class StructBarrierBagBuilder
{
    [GpuDeviceFunction]
    public static StructBarrierBag Create(ReadOnlySpan<int> values)
    {
        Gpu.SyncThreads();
        StructBarrierBag bag;
        bag.First = values[0];
        return bag;
    }
}

public struct FieldInitWithParamCtorHolder
{
    public int V = StructMemberBarrierKernels.TaggedRotate(1, true);

    public FieldInitWithParamCtorHolder(int x)
    {
        V = x;
    }
}

public struct BarrierDisposable : IDisposable
{
    public int X;

    public void Dispose() => StructMemberBarrierKernels.SyncBlock();
}

public struct PinnableBarrierHolder
{
    public int[] Data;
}

public static partial class PinnableBarrierExtensions
{
    [GpuDeviceFunction]
    public static ref int GetPinnableReference(this PinnableBarrierHolder holder)
    {
        Gpu.SyncThreads();
        return ref holder.Data[0];
    }
}

public struct TruthBarrierHolder
{
    public int X;

    public static TruthBarrierHolder operator &(TruthBarrierHolder a, TruthBarrierHolder b) =>
        new() { X = a.X + b.X };

    public static bool operator true(TruthBarrierHolder a) => a.X != 0;

    public static bool operator false(TruthBarrierHolder a)
    {
        StructMemberBarrierKernels.SyncBlock();
        return a.X == 0;
    }
}

public struct OperatorBarrierHolder
{
    public int X;

    public static OperatorBarrierHolder operator +(OperatorBarrierHolder a, OperatorBarrierHolder b)
    {
        StructMemberBarrierKernels.SyncBlock();
        return new OperatorBarrierHolder { X = a.X + b.X };
    }
}

public struct BranchingSetterBarrier
{
    public int X;

    public int Rotated
    {
        get => X;
        set
        {
            if (value > 0)
            {
                X = StructMemberBarrierKernels.TaggedRotate(value, true);
            }
            else
            {
                X = 0;
            }

            StructMemberBarrierKernels.SyncBlock();
        }
    }
}

public struct UniformGetterExitOnlySetterBarrier
{
    public int X;

    public int Rotated
    {
        get => StructMemberBarrierKernels.TaggedRotate(X, true);
        set
        {
            if (value > 0)
            {
                return;
            }

            X = StructMemberBarrierKernels.TaggedRotate(value, true);
        }
    }
}

public struct ExitOnlyConversionHolder
{
    public int X;

    public static implicit operator int(ExitOnlyConversionHolder holder)
    {
        if (holder.X > 0)
        {
            return 0;
        }

        return StructMemberBarrierKernels.TaggedRotate(holder.X, true);
    }
}

public struct OutUniformBarrierCtor
{
    public int X;

    public OutUniformBarrierCtor(out int y)
    {
        y = 0;
        StructMemberBarrierKernels.SyncBlock();
        X = 1;
    }

    public int Prop
    {
        get => X;
        set
        {
            if (value > 0)
            {
                X = StructMemberBarrierKernels.TaggedRotate(value, true);
            }
            else
            {
                X = 0;
            }

            StructMemberBarrierKernels.SyncBlock();
        }
    }
}

public struct OutVariantBarrierCtor
{
    public int X;

    public OutVariantBarrierCtor(out int y)
    {
        y = ThreadIndex.X;
        StructMemberBarrierKernels.SyncBlock();
        X = 1;
    }

    public int Prop
    {
        get => X;
        set
        {
            if (value > 0)
            {
                X = StructMemberBarrierKernels.TaggedRotate(value, true);
            }
            else
            {
                X = 0;
            }

            StructMemberBarrierKernels.SyncBlock();
        }
    }
}

public struct NullableGetterBarrier
{
    public int? Value;

    public int? Prop
    {
        get
        {
            StructMemberBarrierKernels.SyncBlock();
            return Value;
        }
        set => Value = value;
    }
}

public struct NullableSetterBarrier
{
    public int X;

    public int? Prop
    {
        get => X > 0 ? X : null;
        set
        {
            if ((value ?? 0) > 0)
            {
                X = StructMemberBarrierKernels.TaggedRotate(value ?? 0, true);
            }
            else
            {
                X = 0;
            }

            StructMemberBarrierKernels.SyncBlock();
        }
    }
}

public struct NullableVariantGetterSetterBarrier
{
    public int X;

    public int? Prop
    {
        get => ThreadIndex.X == 0 ? null : 1;
        set
        {
            StructMemberBarrierKernels.SyncBlock();
            X = value ?? 0;
        }
    }
}

public struct NullableIndexerSetterBarrier
{
    public int X;

    public int? this[int index]
    {
        get => X > 0 ? X + index : null;
        set
        {
            if ((value ?? 0) > 0)
            {
                X = StructMemberBarrierKernels.TaggedRotate(value ?? 0, true);
            }
            else
            {
                X = 0;
            }

            StructMemberBarrierKernels.SyncBlock();
        }
    }
}

public struct IndexerSetterOnlyBarrier
{
    public int X;

    public int this[int index]
    {
        get => X + index;
        set => X = StructMemberBarrierKernels.TaggedRotate(value + index, true);
    }
}

public struct InConvBox
{
    public int X;

    public static implicit operator InConvBox(int x)
    {
        if (x > 0)
        {
            StructMemberBarrierKernels.SyncBlock();
        }

        StructMemberBarrierKernels.SyncBlock();
        return new InConvBox { X = x };
    }

    public static int operator +(InConvBox a, InConvBox b) => a.X + b.X;
}

public interface IBarrierAction
{
    void Run();
}

public struct BarrierAction : IBarrierAction
{
    public void Run() => StructMemberBarrierKernels.SyncBlock();
}

public struct PlainAction : IBarrierAction
{
    public void Run()
    {
    }
}

public struct CtorBarrierAction
{
    public int X;

    public CtorBarrierAction()
    {
        StructMemberBarrierKernels.SyncBlock();
        X = 1;
    }
}

public struct DisposableBarrierAction : IDisposable
{
    public void Dispose() => StructMemberBarrierKernels.SyncBlock();
}

public struct GenericConstrainedHolder<T> where T : struct, IBarrierAction
{
    public int Value;

    public GenericConstrainedHolder(int x)
    {
        default(T).Run();
        Value = x;
    }
}

public struct GenericNewTHolder<T> where T : new()
{
    public int Value;

    public GenericNewTHolder(int x)
    {
        _ = new T();
        Value = x;
    }
}

public struct GenericUsingTHolder<T> where T : struct, IDisposable
{
    public int Value;

    public GenericUsingTHolder(int x)
    {
        using var t = default(T);
        Value = x;
    }
}

public struct GenericOuter<T> where T : struct, IBarrierAction
{
    public struct Inner
    {
        public int Value;

        public Inner(int x)
        {
            default(T).Run();
            Value = x;
        }
    }

    public struct Nested<U> where U : struct, IBarrierAction
    {
        public int Value;

        public Nested(int x)
        {
            default(T).Run();
            default(U).Run();
            Value = x;
        }
    }
}

public struct GenericPropBox<T> where T : struct, IBarrierAction
{
    public int Prop
    {
        get
        {
            default(T).Run();
            return 1;
        }
    }
}

public interface IExplicitProp
{
    int Prop { set; }
}

public struct ExplicitPropBarrier : IExplicitProp
{
    public int X;

    int IExplicitProp.Prop
    {
        set
        {
            StructMemberBarrierKernels.SyncBlock();
            X = value;
        }
    }
}

public struct PrimaryBranchingCtor(int x)
{
    public int V = x > 0 ? StructMemberBarrierKernels.TaggedRotate(x, true) : 0;
}

public struct PrimaryMultiFieldCtor(int x)
{
    public int V = x > 0 ? StructMemberBarrierKernels.TaggedRotate(x, true) : 0;
    public int After = StructMemberBarrierKernels.SyncBlockInt();
}

public record struct RecordBranchingCtor(int X)
{
    public int V = X > 0 ? StructMemberBarrierKernels.TaggedRotate(X, true) : 0;
}

public record struct PositionalRecord(int X)
{
    public int Y = 1;
}

public struct ChainedPrimary(int x)
{
    public int V = x > 0 ? StructMemberBarrierKernels.TaggedRotate(x, true) : 0;

    public ChainedPrimary(int a, int b) : this(a + b)
    {
    }
}

public struct ExplicitCtorBranchingFieldInit
{
    public int V = ThreadIndex.X > 0 ? StructMemberBarrierKernels.TaggedRotate(1, true) : 0;

    public ExplicitCtorBranchingFieldInit(int x)
    {
    }
}

public interface IBarrierActionGen
{
    void Run<U>() where U : struct, IBarrierAction;
}

public struct ImplActionGen : IBarrierActionGen
{
    public void Run<U>() where U : struct, IBarrierAction => default(U).Run();
}

public struct GenericConstrainedGenMethodHolder<T, U> where T : struct, IBarrierActionGen where U : struct, IBarrierAction
{
    public int Value;

    public GenericConstrainedGenMethodHolder(int x)
    {
        default(T).Run<U>();
        Value = x;
    }
}

public partial struct PartialIndexerHolder
{
    public int X;

    public partial int this[int index] { get; }

    public partial int this[int index]
    {
        get => X + index;
    }
}
