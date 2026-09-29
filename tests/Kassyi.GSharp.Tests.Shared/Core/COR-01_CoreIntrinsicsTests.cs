using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Core;

public class COR01_CoreIntrinsicsTests
{
    [Fact]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "HappyPath")]
    public void Test_COR01_GpuHint_Unroll_DefaultFactor_ExecutesWithoutException()
    {
        // Happy Path: Gpu.Hint.Unroll is a pure marker method and should complete safely on the host
        var exception = Record.Exception(() => Gpu.Hint.Unroll());
        Assert.Null(exception);
    }

    [Theory]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "Boundary")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Test_COR01_GpuHint_Unroll_BoundaryFactors_ExecutesWithoutException(int factor)
    {
        // Boundary Testing: Extreme values for unroll factor should be accepted without throwing
        var exception = Record.Exception(() => Gpu.Hint.Unroll(factor));
        Assert.Null(exception);
    }

    [Fact]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_GpuSyncAndControl_HostInvocation_ThrowsGpuOnlyException()
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
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_GpuAtomicsAndArithmetics_HostInvocation_ThrowsGpuOnlyException()
    {
        // Sad Path: Atomic and carry arithmetic intrinsics must throw GpuOnlyException
        int intVal = 10;
        uint uintVal = 20;
        ulong ulongVal = 30;
        float floatVal = 40.0f;
        ulong carry = 0;
        long borrow = 0;

        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicAdd(ref intVal, 1));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicAdd(ref uintVal, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicAdd(ref ulongVal, 1ul));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicAdd(ref floatVal, 1.0f));

        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicSub(ref intVal, 1));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicSub(ref uintVal, 1u));

        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicOr(ref uintVal, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicOr(ref ulongVal, 1ul));

        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicMax(ref uintVal, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.AtomicMax(ref ulongVal, 1ul));

        Assert.Throws<GpuOnlyException>(() => Gpu.AddWithCarry(uintVal, 1u, ref carry));
        Assert.Throws<GpuOnlyException>(() => Gpu.SubWithBorrow(uintVal, 1u, ref borrow));

        Assert.Throws<GpuOnlyException>(() => Gpu.BitReverse(uintVal));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyWide(uintVal, 2u));
        Assert.Throws<GpuOnlyException>(() => Gpu.MultiplyHigh(ulongVal, 2ul));
        Assert.Throws<GpuOnlyException>(() => Gpu.FunnelShiftLeft(uintVal, uintVal, 2u));
        Assert.Throws<GpuOnlyException>(() => Gpu.FunnelShiftRight(uintVal, uintVal, 2u));
    }

    [Fact]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_CoordinateProperties_HostEvaluation_ThrowsGpuOnlyException()
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
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_GpuShuffleAndMask_HostInvocation_ThrowsGpuOnlyException()
    {
        // Sad Path: Shuffle and active mask intrinsics on Gpu class must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => Gpu.ActiveMask());

        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleSync(0xFFFFFFFF, 42u, 1));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleSync(0xFFFFFFFF, 42.0f, 1));

        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleDownSync(0xFFFFFFFF, 42, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleDownSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleDownSync(0xFFFFFFFF, 42.0f, 1u));

        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleUpSync(0xFFFFFFFF, 42, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleUpSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleUpSync(0xFFFFFFFF, 42.0f, 1u));

        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleXorSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Gpu.ShuffleXorSync(0xFFFFFFFF, 42.0f, 1));
    }

    [Fact]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_WarpIntrinsics_HostEvaluation_ThrowsGpuOnlyException()
    {
        // Sad Path: Warp-level intrinsics must throw GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42, 1));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42u, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0f, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleDownSync(0xFFFFFFFF, 42.0, 1u));
        Assert.Throws<GpuOnlyException>(() => Warp.ShuffleUpSync(0xFFFFFFFF, 42u, 1u));
    }

    [Fact]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "SadPath")]
    public void Test_COR01_SharedMemory_HostEvaluation_ThrowsGpuOnlyException()
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
    }

    [Theory]
    [Trait("FeatureId", "COR-01")]
    [Trait("Category", "Boundary")]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, -1, 100, -100)]
    [InlineData(int.MinValue, int.MaxValue, int.MinValue, int.MaxValue)]
    public void Test_COR01_VectorTypes_ConstructorAndProperties_RetainValues(int x, int y, int z, int w)
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
}
