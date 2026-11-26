using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class CoreIntrinsicsTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuHintUnroll_DefaultFactor_ExecutesWithoutException()
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
    public void GpuHintUnroll_ExtremeFactors_ExecutesWithoutException(int factor)
    {
        // Boundary Testing: Extreme values for unroll factor should be accepted without throwing
        var exception = Record.Exception(() => Gpu.Hint.Unroll(factor));
        Assert.Null(exception);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuSyncAndControl_HostInvocation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018, GSH0042 // The host call is what this test checks: the GpuOnlyException backstop
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
#pragma warning restore GSH0018, GSH0042
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuArithmetics_HostInvocation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
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

        // Representative ManagedAtomics-forwarded stub host backstop (#728)
        int atomicVal = 7;
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicAdd(ref atomicVal, 1));
        Assert.Equal(7, atomicVal);
#pragma warning restore GSH0018
    }

    // Every stub that forwards to ManagedAtomics, by "<name>(<element type>)"; each call starts from 7 and is
    // launched on the managed CPU path so it reaches the forwarding stub and ManagedAtomics
    private static readonly Dictionary<string, Action> s_forwardedAtomicStubs = new()
    {
        ["AtomicAdd(int)"] = static () => RunInt(0, 8, 7),
        ["AtomicAdd(uint)"] = static () => RunUInt(0, 8u, 7u),
        ["AtomicAdd(ulong)"] = static () => RunULong(0, 8ul, 7ul),
        ["AtomicAdd(float)"] = static () => RunFloat(0, 8f, 7f),
        ["AtomicSub(int)"] = static () => RunInt(1, 6, 7),
        ["AtomicSub(uint)"] = static () => RunUInt(1, 6u, 7u),
        ["AtomicOr(uint)"] = static () => RunUInt(2, 15u, 7u),
        ["AtomicOr(ulong)"] = static () => RunULong(1, 15ul, 7ul),
        ["AtomicMax(uint)"] = static () => RunUInt(3, 9u, 7u),
        ["AtomicMax(ulong)"] = static () => RunULong(2, 9ul, 7ul),
        ["AtomicExch(int)"] = static () => RunInt(2, 1, 7),
        ["AtomicExch(uint)"] = static () => RunUInt(4, 1u, 7u),
        ["AtomicExch(ulong)"] = static () => RunULong(3, 1ul, 7ul),
        ["AtomicExch(float)"] = static () => RunFloat(1, 1f, 7f),
        ["AtomicMin(int)"] = static () => RunInt(3, 1, 7),
        ["AtomicMin(uint)"] = static () => RunUInt(5, 1u, 7u),
        ["AtomicMin(ulong)"] = static () => RunULong(4, 1ul, 7ul),
        ["AtomicInc(uint)"] = static () => RunUInt(6, 8u, 7u),
        ["AtomicDec(uint)"] = static () => RunUInt(7, 6u, 7u),
        ["AtomicCAS(int)"] = static () => RunInt(4, 1, 7),
        ["AtomicCAS(uint)"] = static () => RunUInt(8, 1u, 7u),
        ["AtomicCAS(ulong)"] = static () => RunULong(5, 1ul, 7ul),
        ["AtomicAnd(int)"] = static () => RunInt(5, 1, 7),
        ["AtomicAnd(uint)"] = static () => RunUInt(9, 1u, 7u),
        ["AtomicAnd(ulong)"] = static () => RunULong(6, 1ul, 7ul),
        ["AtomicXor(int)"] = static () => RunInt(6, 6, 7),
        ["AtomicXor(uint)"] = static () => RunUInt(10, 6u, 7u),
        ["AtomicXor(ulong)"] = static () => RunULong(7, 6ul, 7ul),
        ["ReductionAdd(int)"] = static () => RunInt(7, 8, 0, checkReturned: false),
        ["ReductionAdd(uint)"] = static () => RunUInt(11, 8u, 0u, checkReturned: false),
        ["ReductionAdd(ulong)"] = static () => RunULong(8, 8ul, 0ul, checkReturned: false),
        ["ReductionAdd(float)"] = static () => RunFloat(2, 8f, 0f, checkReturned: false),
    };

    public static TheoryData<string> ForwardedAtomicStubs => new(s_forwardedAtomicStubs.Keys);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(ForwardedAtomicStubs))]
    public void ForwardedAtomicStub_ManagedLaunch_ForwardsToManagedAtomics(string stub)
    {
        Assert.True(s_forwardedAtomicStubs.TryGetValue(stub, out var invoke));
        invoke();
    }

    // The table above lists every overload that ptx_intrinsics.yml forwards to a managed member, and nothing else
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ForwardedAtomicStubs_ManagedMemberInspection_CoversEveryOverload()
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

    private static void RunInt(int op, int expectedValue, int expectedReturned, bool checkReturned = true)
    {
        int[] value = [7];
        int[] returned = [0];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            ForwardedAtomicStubKernels.LaunchIntAtomicsKernel(op, value, returned);
        }

        Assert.Equal(expectedValue, value[0]);
        if (checkReturned)
        {
            Assert.Equal(expectedReturned, returned[0]);
        }
    }

    private static void RunUInt(int op, uint expectedValue, uint expectedReturned, bool checkReturned = true)
    {
        uint[] value = [7u];
        uint[] returned = [0u];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            ForwardedAtomicStubKernels.LaunchUIntAtomicsKernel(op, value, returned);
        }

        Assert.Equal(expectedValue, value[0]);
        if (checkReturned)
        {
            Assert.Equal(expectedReturned, returned[0]);
        }
    }

    private static void RunULong(int op, ulong expectedValue, ulong expectedReturned, bool checkReturned = true)
    {
        ulong[] value = [7ul];
        ulong[] returned = [0ul];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            ForwardedAtomicStubKernels.LaunchULongAtomicsKernel(op, value, returned);
        }

        Assert.Equal(expectedValue, value[0]);
        if (checkReturned)
        {
            Assert.Equal(expectedReturned, returned[0]);
        }
    }

    private static void RunFloat(int op, float expectedValue, float expectedReturned, bool checkReturned = true)
    {
        float[] value = [7f];
        float[] returned = [0f];
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            ForwardedAtomicStubKernels.LaunchFloatAtomicsKernel(op, value, returned);
        }

        Assert.Equal(expectedValue, value[0]);
        if (checkReturned)
        {
            Assert.Equal(expectedReturned, returned[0]);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CoordinateProperties_HostEvaluation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
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
#pragma warning restore GSH0018
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuShuffleAndMask_HostInvocation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
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
#pragma warning restore GSH0018
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void WarpIntrinsics_HostEvaluation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
        // Sad Path: Warp-level intrinsics must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0f, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42u, 1u));
#pragma warning restore GSH0018
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemory_HostEvaluation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
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
#pragma warning restore GSH0018
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, -1, 100, -100)]
    [InlineData(int.MinValue, int.MaxValue, int.MinValue, int.MaxValue)]
    public void VectorTypes_ConstructorAndProperties_RetainValues(int x, int y, int z, int w)
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
    public void AddedPtxIntrinsics_HostInvocation_ThrowsGpuOnlyException()
    {
#pragma warning disable GSH0018 // The host call is what this test checks: the GpuOnlyException backstop
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
#pragma warning restore GSH0018
    }
}
