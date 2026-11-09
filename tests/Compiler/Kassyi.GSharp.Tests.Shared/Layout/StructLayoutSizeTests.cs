#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TypeLayoutTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class StructLayoutSizeTests
{
    // StructLayout.Size (#621): the host size is the CLR's max(Size, end of the last field) without rounding up,
    // checked against the runtime itself; the device size is that rounded up to the alignment, as C++ sizeof is
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(nameof(SizedPaddedInt), 16, 4)]
    [InlineData(nameof(SizedOddInt), 6, 4)]
    [InlineData(nameof(SizedBelowFields), 5, 4)]
    [InlineData(nameof(SizedExplicitOdd), 6, 4)]
    [InlineData(nameof(SizedPacked), 7, 1)]
    public void HostLayout_StructLayoutSize_MatchesRuntime(string structName, int expectedSize, int expectedAlignment)
    {
        var runtimeSize = structName switch
        {
            nameof(SizedPaddedInt) => Unsafe.SizeOf<SizedPaddedInt>(),
            nameof(SizedOddInt) => Unsafe.SizeOf<SizedOddInt>(),
            nameof(SizedBelowFields) => Unsafe.SizeOf<SizedBelowFields>(),
            nameof(SizedExplicitOdd) => Unsafe.SizeOf<SizedExplicitOdd>(),
            nameof(SizedPacked) => Unsafe.SizeOf<SizedPacked>(),
            _ => throw new ArgumentOutOfRangeException(nameof(structName))
        };

        foreach (var dialect in new[] { s_cudaDialect, s_cpuDialect })
        {
            var layout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(structName), dialect);
            Assert.True(layout.Host.IsKnown);
            Assert.Equal(expectedSize, runtimeSize);
            Assert.Equal(runtimeSize, layout.Host.Size);
            Assert.Equal(expectedAlignment, layout.Host.Alignment);
        }

        var below = default(SizedBelowFields);
        var belowLayout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SizedBelowFields)), s_cpuDialect);
        AssertFieldOffset(belowLayout.Host, nameof(SizedBelowFields.A), OffsetOf(ref below, ref below.A));
        AssertFieldOffset(belowLayout.Host, nameof(SizedBelowFields.B), OffsetOf(ref below, ref below.B));

        var packed = default(SizedPacked);
        var packedLayout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SizedPacked)), s_cpuDialect);
        AssertFieldOffset(packedLayout.Host, nameof(SizedPacked.B), OffsetOf(ref packed, ref packed.B));
        AssertFieldOffset(packedLayout.Host, nameof(SizedPacked.I), OffsetOf(ref packed, ref packed.I));
    }

    // The device rounds StructLayout.Size up to the alignment (C++ sizeof); the struct is Identical only when that
    // gives the host size, otherwise its stride differs and the launcher converts it
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(nameof(SizedPaddedInt), 16, 4, TransferClass.Identical)]
    [InlineData(nameof(SizedOddInt), 8, 4, TransferClass.Convert)]
    [InlineData(nameof(SizedBelowFields), 8, 4, TransferClass.Convert)]
    [InlineData(nameof(SizedExplicitOdd), 8, 4, TransferClass.Convert)]
    [InlineData(nameof(SizedPacked), 7, 1, TransferClass.Identical)]
    public void DeviceLayout_StructLayoutSize_RoundsUpToAlignment(string structName, int expectedSize,
        int expectedAlignment, TransferClass expectedTransfer)
    {
        foreach (var dialect in new[] { s_cudaDialect, s_cpuDialect })
        {
            var layout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(structName), dialect);

            Assert.Equal(new TypeLayout(expectedSize, expectedAlignment), layout.Device);
            Assert.Equal(layout.Device, TypeLayoutCalculator.GetDeviceLayout(GetStructSymbol(structName), dialect));
            Assert.Equal(expectedTransfer, layout.Transfer);
            if (layout.Transfer == TransferClass.Identical)
            {
                Assert.Equal(layout.Host.Size, layout.Device.Size);
            }
        }
    }

    // A sized struct as a field, a sized struct of a 16-byte aligned vector and a sized generic struct: the host layout
    // is the runtime's, the device one the C++ compiler's
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostAndDeviceLayout_StructLayoutSizeNestedVectorAndGeneric_MatchRuntime()
    {
        var nested = default(SizedNestedOuter);
        var nestedCpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SizedNestedOuter)), s_cpuDialect);
        Assert.Equal(8, Unsafe.SizeOf<SizedNestedOuter>());
        Assert.Equal(Unsafe.SizeOf<SizedNestedOuter>(), nestedCpu.Host.Size);
        AssertFieldOffset(nestedCpu.Host, nameof(SizedNestedOuter.B), OffsetOf(ref nested, ref nested.B));
        Assert.Equal(6, OffsetOf(ref nested, ref nested.B));
        Assert.Equal(new TypeLayout(12, 4), nestedCpu.Device);
        Assert.Equal(TransferClass.Convert, nestedCpu.Transfer);

        var vectorCuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SizedVector4)), s_cudaDialect);
        var vectorCpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SizedVector4)), s_cpuDialect);
        Assert.Equal(20, Unsafe.SizeOf<SizedVector4>());
        Assert.Equal(Unsafe.SizeOf<SizedVector4>(), vectorCuda.Host.Size);
        Assert.Equal(new TypeLayout(32, 16), vectorCuda.Device);
        Assert.Equal(TransferClass.Convert, vectorCuda.Transfer);
        Assert.Equal(new TypeLayout(20, 4), vectorCpu.Device);
        Assert.Equal(TransferClass.Identical, vectorCpu.Transfer);

        var (compilation, _, _) = GetContext();
        var genericDouble = GetStructSymbol("SizedGeneric")
            .Construct(compilation.GetSpecialType(SpecialType.System_Double));
        foreach (var dialect in new[] { s_cudaDialect, s_cpuDialect })
        {
            var generic = TypeLayoutCalculator.GetStructLayout(genericDouble, dialect);
            Assert.Equal(12, Unsafe.SizeOf<SizedGeneric<double>>());
            Assert.Equal(Unsafe.SizeOf<SizedGeneric<double>>(), generic.Host.Size);
            Assert.Equal(new TypeLayout(16, 8), generic.Device);
            Assert.Equal(TransferClass.Convert, generic.Transfer);
        }
    }

    // PcSized (captured parameter 'a', Vector2 V, Size = 32): the CPU device lays it out as the CLR does (Identical),
    // CUDA moves V to offset 8 (Convert); both are 32 bytes, as the CLR is
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostAndDeviceLayout_StructLayoutSizeWithAlignedVector_DifferBetweenCudaAndCpu()
    {
        var symbol = RoslynTestUtility.GetNamedTypeSymbol("PcSized");

        var cuda = TypeLayoutCalculator.GetStructLayout(symbol, s_cudaDialect);
        var cpu = TypeLayoutCalculator.GetStructLayout(symbol, s_cpuDialect);

        Assert.Equal(32, cuda.Host.Size);
        Assert.Equal(new TypeLayout(32, 8), cuda.Device);
        Assert.Equal(TransferClass.Convert, cuda.Transfer);
        Assert.Equal(32, cpu.Host.Size);
        Assert.Equal(new TypeLayout(32, 4), cpu.Device);
        Assert.Equal(TransferClass.Identical, cpu.Transfer);
    }
}
