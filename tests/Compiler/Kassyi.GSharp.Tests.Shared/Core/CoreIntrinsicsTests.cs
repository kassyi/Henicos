using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class CoreIntrinsicsTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GpuHint_Unroll_DefaultFactor_ExecutesWithoutException()
    {
        // Happy Path: Gpu.Hint.Unroll is a pure marker method and should complete safely on the host
        var exception = Record.Exception(() => Gpu.Hint.Unroll());
        Assert.Null(exception);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Test_GpuHint_Unroll_BoundaryFactors_ExecutesWithoutException(int factor)
    {
        // Boundary Testing: Extreme values for unroll factor should be accepted without throwing
        var exception = Record.Exception(() => Gpu.Hint.Unroll(factor));
        Assert.Null(exception);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuSyncAndControl_HostInvocation_ThrowsGpuOnlyException()
    {
        // Sad Path: Hardware sync & control markers must throw GpuOnlyException when invoked on the host CPU
        Assert.Throws<GpuOnlyException>(() => Gpu.SyncThreads());
        Assert.Throws<GpuOnlyException>(() => Gpu.SyncGrid());
        Assert.Throws<GpuOnlyException>(() => Gpu.SyncGrid(() => { }));
        Assert.Throws<GpuOnlyException>(() => Gpu.Constant("TEST_MACRO"));
        Assert.Throws<GpuOnlyException>(() => Gpu.InlinePtx("nop;"));
        Assert.Throws<GpuOnlyException>(() => Gpu.ForUnroll(0, 4, _ => { }));
        
        int ctx = 0;
        Assert.Throws<GpuOnlyException>(() => Gpu.ForUnroll(ref ctx, 0, 4, (ref int c, int i) => { }));
        Assert.Throws<GpuOnlyException>(() => Gpu.GridStrideLoop(10, _ => { }));
        Assert.Throws<GpuOnlyException>(() => Gpu.GridStrideLoop(ref ctx, 10, (ref int c, int i) => { }));
        Assert.Throws<GpuOnlyException>(() => Gpu.SingleThread(() => { }));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuArithmetics_HostInvocation_ThrowsGpuOnlyException()
    {
        // Sad Path: Carry arithmetic intrinsics must throw GpuOnlyException (the atomics: the theory below)
        uint uintVal = 20;
        ulong ulongVal = 30;
        ulong carry = 0;
        long borrow = 0;

        Assert.Throws<GpuOnlyException>(() => Gpu.AddWithCarry(uintVal, 1u, ref carry));
        Assert.Throws<GpuOnlyException>(() => Gpu.SubWithBorrow(uintVal, 1u, ref borrow));

        Assert.Throws<GpuOnlyException>(() => Gpu.BitReverse(uintVal));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyWide(uintVal, 2u));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyHigh(ulongVal, 2ul));
        Assert.Throws<GpuOnlyException>(() => Gpu.FunnelShiftLeft(uintVal, uintVal, 2u));
        Assert.Throws<GpuOnlyException>(() => Gpu.FunnelShiftRight(uintVal, uintVal, 2u));
    }

    // Every stub that forwards to ManagedAtomics, by "<name>(<element type>)"; each call starts from 7 and must leave it
    private static readonly Dictionary<string, Action> s_forwardedAtomicStubs = new()
    {
        ["AtomicAdd(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicAdd(ref v, 1)),
        ["AtomicAdd(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicAdd(ref v, 1u)),
        ["AtomicAdd(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicAdd(ref v, 1ul)),
        ["AtomicAdd(float)"] = static () => Run(7f, static (ref float v) => Gpu.AtomicAdd(ref v, 1f)),
        ["AtomicSub(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicSub(ref v, 1)),
        ["AtomicSub(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicSub(ref v, 1u)),
        ["AtomicOr(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicOr(ref v, 8u)),
        ["AtomicOr(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicOr(ref v, 8ul)),
        ["AtomicMax(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicMax(ref v, 9u)),
        ["AtomicMax(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicMax(ref v, 9ul)),
        ["AtomicExch(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicExch(ref v, 1)),
        ["AtomicExch(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicExch(ref v, 1u)),
        ["AtomicExch(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicExch(ref v, 1ul)),
        ["AtomicExch(float)"] = static () => Run(7f, static (ref float v) => Gpu.AtomicExch(ref v, 1f)),
        ["AtomicMin(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicMin(ref v, 1)),
        ["AtomicMin(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicMin(ref v, 1u)),
        ["AtomicMin(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicMin(ref v, 1ul)),
        ["AtomicInc(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicInc(ref v, 9u)),
        ["AtomicDec(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicDec(ref v, 9u)),
        ["AtomicCAS(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicCAS(ref v, 7, 1)),
        ["AtomicCAS(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicCAS(ref v, 7u, 1u)),
        ["AtomicCAS(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicCAS(ref v, 7ul, 1ul)),
        ["AtomicAnd(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicAnd(ref v, 1)),
        ["AtomicAnd(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicAnd(ref v, 1u)),
        ["AtomicAnd(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicAnd(ref v, 1ul)),
        ["AtomicXor(int)"] = static () => Run(7, static (ref int v) => Gpu.AtomicXor(ref v, 1)),
        ["AtomicXor(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.AtomicXor(ref v, 1u)),
        ["AtomicXor(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.AtomicXor(ref v, 1ul)),
        ["ReductionAdd(int)"] = static () => Run(7, static (ref int v) => Gpu.ReductionAdd(ref v, 1)),
        ["ReductionAdd(uint)"] = static () => Run(7u, static (ref uint v) => Gpu.ReductionAdd(ref v, 1u)),
        ["ReductionAdd(ulong)"] = static () => Run(7ul, static (ref ulong v) => Gpu.ReductionAdd(ref v, 1ul)),
        ["ReductionAdd(float)"] = static () => Run(7f, static (ref float v) => Gpu.ReductionAdd(ref v, 1f)),
    };

    public static TheoryData<string> ForwardedAtomicStubs => new(s_forwardedAtomicStubs.Keys);

    // Outside a managed launch a stub that forwards to ManagedAtomics still throws, and throws before it writes (#728)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(ForwardedAtomicStubs))]
    public void Test_ForwardedAtomicStub_HostInvocation_ThrowsGpuOnlyExceptionAndLeavesTheValue(string stub)
    {
        s_forwardedAtomicStubs[stub]();
    }

    // The table above lists every overload that ptx_intrinsics.yml forwards to a managed member, and nothing else
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ForwardedAtomicStubs_CoverEveryOverloadWithAManagedMember()
    {
        var yaml = File.ReadAllText(RepositoryPaths.Combine("src", "NativeApi", "ptx_intrinsics.yml"));
        Assert.True(NativeApiYamlReader.TryRead(yaml, out var definition, out var error), error?.Message);

        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in definition.Methods)
        {
            // Warp.ActiveMask forwards to ManagedGpuContext; its host invocation is checked with the other warp intrinsics
            if (method.ManagedName is null || !method.ManagedName.StartsWith("ManagedAtomics.", StringComparison.Ordinal))
            {
                continue;
            }

            if (method.TypeTemplate is { } template)
            {
                foreach (var type in template.Types)
                {
                    expected.Add($"{method.Name}({type})");
                }
            }
            else
            {
                // AtomicInc / AtomicDec: the type of the first parameter without its "ref "
                expected.Add($"{method.Name}({method.Parameters[0].Type["ref ".Length..]})");
            }
        }

        Assert.Equal(expected, new SortedSet<string>(s_forwardedAtomicStubs.Keys, StringComparer.Ordinal));
    }

    private delegate void RefAction<T>(ref T value);

    // The call throws GpuOnlyException and leaves the value as it was
    private static void Run<T>(T initial, RefAction<T> call)
    {
        var value = initial;
        Assert.Throws<GpuOnlyException>(() => call(ref value));
        Assert.Equal(initial, value);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CoordinateProperties_HostEvaluation_ThrowsGpuOnlyException()
    {
        // Sad Path: Thread/Block coordinate markers must throw GpuOnlyException on CPU
        var dim = new Dim3();
        Assert.Throws<GpuOnlyException>(() => _ = dim.X);
        Assert.Throws<GpuOnlyException>(() => _ = dim.Y);
        Assert.Throws<GpuOnlyException>(() => _ = dim.Z);

        Assert.Throws<GpuOnlyException>(() => _ = ThreadIndex.X);
        Assert.Throws<GpuOnlyException>(() => _ = ThreadIndex.Y);
        Assert.Throws<GpuOnlyException>(() => _ = ThreadIndex.Z);

        Assert.Throws<GpuOnlyException>(() => _ = BlockIndex.X);
        Assert.Throws<GpuOnlyException>(() => _ = BlockIndex.Y);
        Assert.Throws<GpuOnlyException>(() => _ = BlockIndex.Z);

        Assert.Throws<GpuOnlyException>(() => _ = BlockDimension.X);
        Assert.Throws<GpuOnlyException>(() => _ = BlockDimension.Y);
        Assert.Throws<GpuOnlyException>(() => _ = BlockDimension.Z);

        Assert.Throws<GpuOnlyException>(() => _ = GridDimension.X);
        Assert.Throws<GpuOnlyException>(() => _ = GridDimension.Y);
        Assert.Throws<GpuOnlyException>(() => _ = GridDimension.Z);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuShuffleAndMask_HostInvocation_ThrowsGpuOnlyException()
    {
        // Sad Path: Shuffle and active mask intrinsics on Gpu class must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => Warp.ActiveMask());

        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleSync(0xFFFFFFFF, 42u, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleSync(0xFFFFFFFF, 42.0f, 1));

        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0f, 1u));

        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42.0f, 1u));

        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleXorSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleXorSync(0xFFFFFFFF, 42.0f, 1));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_WarpIntrinsics_HostEvaluation_ThrowsGpuOnlyException()
    {
        // Sad Path: Warp-level intrinsics must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0f, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42u, 1u));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_SharedMemory_HostEvaluation_ThrowsGpuOnlyException()
    {
        // Happy Path: Constructing SharedMemory on host does not throw, since it's just a marker
        var _ = new SharedMemory<int>(128);

        // Sad Path: Accessing elements on host must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() =>
        {
            var sm = new SharedMemory<int>(128);
            var val = sm[0];
        });
        Assert.Throws<GpuOnlyException>(() =>
        {
            var sm = new SharedMemory<int>(128);
            sm[0] = 42;
        });
        Assert.Throws<GpuOnlyException>(() =>
        {
            var sm = new SharedMemory<int>(128);
            ref int r = ref sm[0];
        });
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, -1, 100, -100)]
    [InlineData(int.MinValue, int.MaxValue, int.MinValue, int.MaxValue)]
    public void Test_VectorTypes_ConstructorAndProperties_RetainValues(int x, int y, int z, int w)
    {
        // Boundary Testing: Verify all vector struct types faithfully retain values across coordinates
        var int2 = new Int2(x, y);
        Assert.Equal(x, int2.X);
        Assert.Equal(y, int2.Y);

        var int3 = new Int3(x, y, z);
        Assert.Equal(x, int3.X);
        Assert.Equal(y, int3.Y);
        Assert.Equal(z, int3.Z);

        var int4 = new Int4(x, y, z, w);
        Assert.Equal(x, int4.X);
        Assert.Equal(y, int4.Y);
        Assert.Equal(z, int4.Z);
        Assert.Equal(w, int4.W);

        var f3 = new Float3(x, y, z);
        Assert.Equal((float)x, f3.X);
        Assert.Equal((float)y, f3.Y);
        Assert.Equal((float)z, f3.Z);

        var d3 = new Double3(x, y, z);
        Assert.Equal((double)x, d3.X);
        Assert.Equal((double)y, d3.Y);
        Assert.Equal((double)z, d3.Z);

        var d4 = new Double4(x, y, z, w);
        Assert.Equal((double)x, d4.X);
        Assert.Equal((double)y, d4.Y);
        Assert.Equal((double)z, d4.Z);
        Assert.Equal((double)w, d4.W);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_AddedPtxIntrinsics_HostInvocation_ThrowsGpuOnlyException()
    {
        // Verify all Phase 3 PTX Intrinsics throw GpuOnlyException when invoked on the host CPU
        uint a = 10, b = 20;
        ulong c = 30, carry = 0;
        int sa = 10, sb = 20;
        long sc = 30;

        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyAddWide(a, b, c));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyAddWide(sa, sb, sc));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyAddWideWithCarry(a, b, c, ref carry));

        Assert.Throws<GpuOnlyException>(() => Gpu.LaneId());
        Assert.Throws<GpuOnlyException>(() => Gpu.WarpId());
        Assert.Throws<GpuOnlyException>(() => Gpu.SmId());

        int intVal = 42;
        Assert.Throws<GpuOnlyException>(() => Gpu.LoadCachedGlobal(ref intVal));
        Assert.Throws<GpuOnlyException>(() => Gpu.StoreCachedGlobal(ref intVal, 100));
        Assert.Throws<GpuOnlyException>(() => Gpu.Prefetch(IntPtr.Zero));
        Assert.Throws<GpuOnlyException>(() => Gpu.PrefetchUniform(IntPtr.Zero));
        Assert.Throws<GpuOnlyException>(() => Gpu.ApplyPriorityL2(IntPtr.Zero, 128, 0.5f));
        Assert.Throws<GpuOnlyException>(() => Gpu.ConvertToSharedMemory(ref intVal));

        int sharedInt = 0, globalInt = 0;
        Vector4 sharedVec4 = default, globalVec4 = default;
        Assert.Throws<GpuOnlyException>(() => Gpu.CopyAsync(ref sharedInt, in globalInt));
        Assert.Throws<GpuOnlyException>(() => Gpu.CopyAsync(ref sharedVec4, in globalVec4));
        Assert.Throws<GpuOnlyException>(() => Gpu.CopyAsyncCommitGroup());
        Assert.Throws<GpuOnlyException>(() => Gpu.CopyAsyncWaitGroup(0));
        Assert.Throws<GpuOnlyException>(() => Gpu.CopyAsyncWaitAll());

        ulong barrier = 0;
        Assert.Throws<GpuOnlyException>(() => Gpu.MBarrierInit(ref barrier, 32));
        Assert.Throws<GpuOnlyException>(() => Gpu.MBarrierArrive(ref barrier));
        Assert.Throws<GpuOnlyException>(() => Gpu.MBarrierWait(ref barrier, 0));

        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> fragC = default;
        sbyte elemSByte = 0;
        int elemInt = 0;
        Assert.Throws<GpuOnlyException>(() => Wmma.Fill(ref fragC, 0));
        Assert.Throws<GpuOnlyException>(() => Wmma.Load(ref fragA, in elemSByte, 16));
        Assert.Throws<GpuOnlyException>(() => Wmma.Load(ref fragB, in elemSByte, 16));
        Assert.Throws<GpuOnlyException>(() => Wmma.Load(ref fragC, in elemInt, 16, WmmaMemoryLayout.RowMajor));
        Assert.Throws<GpuOnlyException>(() => Wmma.Store(ref elemInt, in fragC, 16, WmmaMemoryLayout.RowMajor));
        Assert.Throws<GpuOnlyException>(() => Wmma.MultiplyAccumulate(ref fragC, in fragA, in fragB, in fragC));
    }
}
