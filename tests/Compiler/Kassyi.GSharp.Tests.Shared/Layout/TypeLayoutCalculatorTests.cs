using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class TypeLayoutCalculatorTests
{
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();
    private static readonly ITranspilerDialect s_cpuDialect = new StandardCppDialect();

    private static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetContext() =>
        RoslynTestUtility.GetTestCaseContext(Path.Combine("Layout", "HostLayoutCases.cs"));

    private static INamedTypeSymbol GetStructSymbol(string structName)
    {
        var (_, tree, model) = GetContext();
        var decl = tree.GetRoot().DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .First(s => s.Identifier.Text == structName);
        return Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(decl));
    }

    private static int OffsetOf<TStruct, TField>(ref TStruct s, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref s), ref Unsafe.As<TField, byte>(ref field));

    private static void AssertFieldOffset(HostLayout host, string fieldName, int expectedOffset)
    {
        var field = host.Fields.First(f => f.Member.Name == fieldName);
        Assert.Equal(expectedOffset, field.Offset);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void HostLayout_SimpleAndBoolCharAndEnumPointers_MatchRuntime()
    {
        var simple = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SimpleSequential)), s_cudaDialect);
        Assert.True(simple.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<SimpleSequential>(), simple.Host.Size);
        var s1 = default(SimpleSequential);
        AssertFieldOffset(simple.Host, nameof(SimpleSequential.A), OffsetOf(ref s1, ref s1.A));
        AssertFieldOffset(simple.Host, nameof(SimpleSequential.B), OffsetOf(ref s1, ref s1.B));
        AssertFieldOffset(simple.Host, nameof(SimpleSequential.C), OffsetOf(ref s1, ref s1.C));
        Assert.Equal(TransferClass.Identical, simple.Transfer);

        var boolChar = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(BoolAndCharSequential)), s_cudaDialect);
        Assert.True(boolChar.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<BoolAndCharSequential>(), boolChar.Host.Size);
        var s2 = default(BoolAndCharSequential);
        AssertFieldOffset(boolChar.Host, nameof(BoolAndCharSequential.Flag1), OffsetOf(ref s2, ref s2.Flag1));
        AssertFieldOffset(boolChar.Host, nameof(BoolAndCharSequential.Flag2), OffsetOf(ref s2, ref s2.Flag2));
        AssertFieldOffset(boolChar.Host, nameof(BoolAndCharSequential.Ch), OffsetOf(ref s2, ref s2.Ch));
        AssertFieldOffset(boolChar.Host, nameof(BoolAndCharSequential.Value), OffsetOf(ref s2, ref s2.Value));
        Assert.Equal(TransferClass.Identical, boolChar.Transfer);

        var enumPtr = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(EnumAndPointerSequential)), s_cudaDialect);
        Assert.True(enumPtr.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<EnumAndPointerSequential>(), enumPtr.Host.Size);
        var s3 = default(EnumAndPointerSequential);
        AssertFieldOffset(enumPtr.Host, nameof(EnumAndPointerSequential.ByteEnum), OffsetOf(ref s3, ref s3.ByteEnum));
        unsafe
        {
            var ptrOffset = (int)((byte*)&s3.Ptr - (byte*)&s3);
            AssertFieldOffset(enumPtr.Host, nameof(EnumAndPointerSequential.Ptr), ptrOffset);
        }
        AssertFieldOffset(enumPtr.Host, nameof(EnumAndPointerSequential.LongEnum), OffsetOf(ref s3, ref s3.LongEnum));
        AssertFieldOffset(enumPtr.Host, nameof(EnumAndPointerSequential.NativeInt), OffsetOf(ref s3, ref s3.NativeInt));
        AssertFieldOffset(enumPtr.Host, nameof(EnumAndPointerSequential.NativeUInt), OffsetOf(ref s3, ref s3.NativeUInt));
        Assert.Equal(TransferClass.Identical, enumPtr.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostLayout_PackedStructs_MatchRuntime()
    {
        var pack1 = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(Packed1Struct)), s_cudaDialect);
        Assert.True(pack1.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<Packed1Struct>(), pack1.Host.Size);
        var p1 = default(Packed1Struct);
        AssertFieldOffset(pack1.Host, nameof(Packed1Struct.A), OffsetOf(ref p1, ref p1.A));
        AssertFieldOffset(pack1.Host, nameof(Packed1Struct.B), OffsetOf(ref p1, ref p1.B));
        AssertFieldOffset(pack1.Host, nameof(Packed1Struct.C), OffsetOf(ref p1, ref p1.C));
        AssertFieldOffset(pack1.Host, nameof(Packed1Struct.D), OffsetOf(ref p1, ref p1.D));
        Assert.Equal(TransferClass.Identical, pack1.Transfer);

        var pack2 = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(Packed2Struct)), s_cudaDialect);
        Assert.True(pack2.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<Packed2Struct>(), pack2.Host.Size);
        var p2 = default(Packed2Struct);
        AssertFieldOffset(pack2.Host, nameof(Packed2Struct.A), OffsetOf(ref p2, ref p2.A));
        AssertFieldOffset(pack2.Host, nameof(Packed2Struct.B), OffsetOf(ref p2, ref p2.B));
        AssertFieldOffset(pack2.Host, nameof(Packed2Struct.C), OffsetOf(ref p2, ref p2.C));
        AssertFieldOffset(pack2.Host, nameof(Packed2Struct.D), OffsetOf(ref p2, ref p2.D));
        Assert.Equal(TransferClass.Identical, pack2.Transfer);

        var pack4 = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(Packed4Struct)), s_cudaDialect);
        Assert.True(pack4.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<Packed4Struct>(), pack4.Host.Size);
        var p4 = default(Packed4Struct);
        AssertFieldOffset(pack4.Host, nameof(Packed4Struct.A), OffsetOf(ref p4, ref p4.A));
        AssertFieldOffset(pack4.Host, nameof(Packed4Struct.B), OffsetOf(ref p4, ref p4.B));
        AssertFieldOffset(pack4.Host, nameof(Packed4Struct.C), OffsetOf(ref p4, ref p4.C));
        Assert.Equal(TransferClass.Identical, pack4.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostLayout_ExplicitLayout_MatchesRuntimeAndClassifiesAsIdentical()
    {
        var unionLayout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(ExplicitUnionStruct)), s_cudaDialect);
        Assert.Equal(TypeLayoutKind.Explicit, unionLayout.Kind);
        Assert.True(unionLayout.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<ExplicitUnionStruct>(), unionLayout.Host.Size);
        var u = default(ExplicitUnionStruct);
        AssertFieldOffset(unionLayout.Host, nameof(ExplicitUnionStruct.AsInt), OffsetOf(ref u, ref u.AsInt));
        AssertFieldOffset(unionLayout.Host, nameof(ExplicitUnionStruct.AsFloat), OffsetOf(ref u, ref u.AsFloat));
        AssertFieldOffset(unionLayout.Host, nameof(ExplicitUnionStruct.Tail), OffsetOf(ref u, ref u.Tail));
        // The device puts AsInt and AsFloat into one union at offset 0, as the host does
        Assert.Equal(
            0,
            unionLayout.DeviceFields.First(f => f.Member.Name == nameof(ExplicitUnionStruct.AsFloat)).Offset);
        Assert.Equal(unionLayout.Host.Size, unionLayout.Device.Size);
        Assert.Equal(TransferClass.Identical, unionLayout.Transfer);

        var gapLayout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(ExplicitGapWithSizeStruct)), s_cudaDialect);
        Assert.Equal(TypeLayoutKind.Explicit, gapLayout.Kind);
        Assert.True(gapLayout.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<ExplicitGapWithSizeStruct>(), gapLayout.Host.Size);
        var g = default(ExplicitGapWithSizeStruct);
        AssertFieldOffset(gapLayout.Host, nameof(ExplicitGapWithSizeStruct.First), OffsetOf(ref g, ref g.First));
        AssertFieldOffset(gapLayout.Host, nameof(ExplicitGapWithSizeStruct.Middle), OffsetOf(ref g, ref g.Middle));
        AssertFieldOffset(gapLayout.Host, nameof(ExplicitGapWithSizeStruct.Last), OffsetOf(ref g, ref g.Last));
        // The gaps and the trailing bytes up to Size = 24 are padding on the device
        Assert.Equal(24, gapLayout.Device.Size);
        Assert.Equal(TransferClass.Identical, gapLayout.Transfer);

        var explicitPacked = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(ExplicitPackedStruct)), s_cudaDialect);
        Assert.Equal(TypeLayoutKind.Explicit, explicitPacked.Kind);
        Assert.True(explicitPacked.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<ExplicitPackedStruct>(), explicitPacked.Host.Size);
        var ep = default(ExplicitPackedStruct);
        AssertFieldOffset(explicitPacked.Host, nameof(ExplicitPackedStruct.Head), OffsetOf(ref ep, ref ep.Head));
        AssertFieldOffset(explicitPacked.Host, nameof(ExplicitPackedStruct.Value), OffsetOf(ref ep, ref ep.Value));
        Assert.Equal(TransferClass.Identical, explicitPacked.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void HostLayout_AutoAndReferenceContaining_AreNotKnown()
    {
        var autoLayout = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(AutoLayoutCase)), s_cudaDialect);
        Assert.Equal(TypeLayoutKind.Auto, autoLayout.Kind);
        Assert.False(autoLayout.Host.IsKnown);
        Assert.Equal(TransferClass.Convert, autoLayout.Transfer);

        var directArray = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(DirectArrayFieldStruct)), s_cudaDialect);
        Assert.False(directArray.Host.IsKnown);
        Assert.Equal(TransferClass.Convert, directArray.Transfer);

        var nestedArray = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(NestedArrayFieldStruct)), s_cudaDialect);
        Assert.False(nestedArray.Host.IsKnown);
        Assert.Equal(TransferClass.Convert, nestedArray.Transfer);

        var inlineWithArray = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(InlineArrayWithArrayElement)), s_cudaDialect);
        Assert.False(inlineWithArray.Host.IsKnown);
        // Each element is copied with its array field (S-26)
        Assert.Equal(TransferClass.Convert, inlineWithArray.Transfer);

        var outerWithInlineArray = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(OuterWithInlineArrayOfArrayElement)), s_cudaDialect);
        Assert.False(outerWithInlineArray.Host.IsKnown);
        Assert.Equal(TransferClass.Convert, outerWithInlineArray.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void HostLayout_NestedInlineArrayFixedBufferGenericNullable_MatchRuntime()
    {
        var nested = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(NestedOuterStruct)), s_cudaDialect);
        Assert.True(nested.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<NestedOuterStruct>(), nested.Host.Size);
        var n = default(NestedOuterStruct);
        AssertFieldOffset(nested.Host, nameof(NestedOuterStruct.Prefix), OffsetOf(ref n, ref n.Prefix));
        AssertFieldOffset(nested.Host, nameof(NestedOuterStruct.Inner), OffsetOf(ref n, ref n.Inner));
        AssertFieldOffset(nested.Host, nameof(NestedOuterStruct.Suffix), OffsetOf(ref n, ref n.Suffix));
        Assert.Equal(TransferClass.Identical, nested.Transfer);

        var inlineStruct = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(StructWithInlineFourInts)), s_cudaDialect);
        Assert.True(inlineStruct.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<StructWithInlineFourInts>(), inlineStruct.Host.Size);
        var inl = default(StructWithInlineFourInts);
        AssertFieldOffset(inlineStruct.Host, nameof(StructWithInlineFourInts.Tag), OffsetOf(ref inl, ref inl.Tag));
        AssertFieldOffset(inlineStruct.Host, nameof(StructWithInlineFourInts.Values), OffsetOf(ref inl, ref inl.Values));
        AssertFieldOffset(inlineStruct.Host, nameof(StructWithInlineFourInts.Tail), OffsetOf(ref inl, ref inl.Tail));
        Assert.Equal(TransferClass.Identical, inlineStruct.Transfer);

        var fixedBuf = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(FixedBufferStruct)), s_cudaDialect);
        Assert.True(fixedBuf.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<FixedBufferStruct>(), fixedBuf.Host.Size);
        var fb = default(FixedBufferStruct);
        AssertFieldOffset(fixedBuf.Host, nameof(FixedBufferStruct.Header), OffsetOf(ref fb, ref fb.Header));
        unsafe
        {
            AssertFieldOffset(fixedBuf.Host, nameof(FixedBufferStruct.Data), (int)((byte*)fb.Data - (byte*)&fb));
        }
        AssertFieldOffset(fixedBuf.Host, nameof(FixedBufferStruct.Footer), OffsetOf(ref fb, ref fb.Footer));
        Assert.Equal(TransferClass.Identical, fixedBuf.Transfer);

        var genericHolder = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(GenericBoxHolder)), s_cudaDialect);
        Assert.True(genericHolder.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<GenericBoxHolder>(), genericHolder.Host.Size);
        var gh = default(GenericBoxHolder);
        AssertFieldOffset(genericHolder.Host, nameof(GenericBoxHolder.IntBox), OffsetOf(ref gh, ref gh.IntBox));
        AssertFieldOffset(genericHolder.Host, nameof(GenericBoxHolder.DoubleBox), OffsetOf(ref gh, ref gh.DoubleBox));
        AssertFieldOffset(genericHolder.Host, nameof(GenericBoxHolder.Float4Box), OffsetOf(ref gh, ref gh.Float4Box));
        Assert.Equal(TransferClass.Convert, genericHolder.Transfer);
        Assert.Equal(TransferClass.Identical, TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(GenericBoxHolder)), s_cpuDialect).Transfer);

        var nullableStruct = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(NullableFieldsStruct)), s_cudaDialect);
        Assert.True(nullableStruct.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<NullableFieldsStruct>(), nullableStruct.Host.Size);
        var nf = default(NullableFieldsStruct);
        AssertFieldOffset(nullableStruct.Host, nameof(NullableFieldsStruct.Prefix), OffsetOf(ref nf, ref nf.Prefix));
        AssertFieldOffset(nullableStruct.Host, nameof(NullableFieldsStruct.NullableInt), OffsetOf(ref nf, ref nf.NullableInt));
        AssertFieldOffset(nullableStruct.Host, nameof(NullableFieldsStruct.NullableDouble), OffsetOf(ref nf, ref nf.NullableDouble));
        AssertFieldOffset(nullableStruct.Host, nameof(NullableFieldsStruct.NullableFloat2), OffsetOf(ref nf, ref nf.NullableFloat2));

        var emptyStruct = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(EmptyUserStruct)), s_cudaDialect);
        Assert.True(emptyStruct.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<EmptyUserStruct>(), emptyStruct.Host.Size);
        Assert.Equal(1, emptyStruct.Device.Size);
        Assert.Equal(1, emptyStruct.Device.Alignment);
        Assert.Equal(TransferClass.Identical, emptyStruct.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostAndDeviceLayout_VectorsAndDivergentInlineArrays_DifferBetweenCudaAndCpu()
    {
        var sysVecCuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SystemNumericsVectorsStruct)), s_cudaDialect);
        var sysVecCpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(SystemNumericsVectorsStruct)), s_cpuDialect);
        Assert.True(sysVecCuda.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), sysVecCuda.Host.Size);
        var sv = default(SystemNumericsVectorsStruct);
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.Tag1), OffsetOf(ref sv, ref sv.Tag1));
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.V2), OffsetOf(ref sv, ref sv.V2));
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.Tag2), OffsetOf(ref sv, ref sv.Tag2));
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.V3), OffsetOf(ref sv, ref sv.V3));
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.Tag3), OffsetOf(ref sv, ref sv.Tag3));
        AssertFieldOffset(sysVecCuda.Host, nameof(SystemNumericsVectorsStruct.V4), OffsetOf(ref sv, ref sv.V4));
        Assert.Equal(TransferClass.Convert, sysVecCuda.Transfer);
        Assert.Equal(TransferClass.Identical, sysVecCpu.Transfer);

        var core32Cuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(CoreVectorsFloatIntUIntStruct)), s_cudaDialect);
        var core32Cpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(CoreVectorsFloatIntUIntStruct)), s_cpuDialect);
        Assert.True(core32Cuda.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<CoreVectorsFloatIntUIntStruct>(), core32Cuda.Host.Size);
        var cv32 = default(CoreVectorsFloatIntUIntStruct);
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.F2), OffsetOf(ref cv32, ref cv32.F2));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.F3), OffsetOf(ref cv32, ref cv32.F3));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.F4), OffsetOf(ref cv32, ref cv32.F4));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.I2), OffsetOf(ref cv32, ref cv32.I2));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.I3), OffsetOf(ref cv32, ref cv32.I3));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.I4), OffsetOf(ref cv32, ref cv32.I4));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.U2), OffsetOf(ref cv32, ref cv32.U2));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.U3), OffsetOf(ref cv32, ref cv32.U3));
        AssertFieldOffset(core32Cuda.Host, nameof(CoreVectorsFloatIntUIntStruct.U4), OffsetOf(ref cv32, ref cv32.U4));
        Assert.Equal(TransferClass.Convert, core32Cuda.Transfer);
        Assert.Equal(TransferClass.Identical, core32Cpu.Transfer);

        var core64Cuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(CoreVectors64BitStruct)), s_cudaDialect);
        var core64Cpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(CoreVectors64BitStruct)), s_cpuDialect);
        Assert.True(core64Cuda.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<CoreVectors64BitStruct>(), core64Cuda.Host.Size);
        var cv64 = default(CoreVectors64BitStruct);
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.D2), OffsetOf(ref cv64, ref cv64.D2));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.D3), OffsetOf(ref cv64, ref cv64.D3));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.D4), OffsetOf(ref cv64, ref cv64.D4));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.L2), OffsetOf(ref cv64, ref cv64.L2));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.L3), OffsetOf(ref cv64, ref cv64.L3));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.L4), OffsetOf(ref cv64, ref cv64.L4));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.UL2), OffsetOf(ref cv64, ref cv64.UL2));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.UL3), OffsetOf(ref cv64, ref cv64.UL3));
        AssertFieldOffset(core64Cuda.Host, nameof(CoreVectors64BitStruct.UL4), OffsetOf(ref cv64, ref cv64.UL4));
        Assert.Equal(TransferClass.Convert, core64Cuda.Transfer);
        Assert.Equal(TransferClass.Identical, core64Cpu.Transfer);

        // InlineArray with Float2 directly (Float2 size=8, align=8 on CUDA vs size=8, align=4 on Host): the stride
        // and the offsets are the same, so the bytes are the same and only the alignment differs -> Identical
        // (design §3.3)
        var inlineFloat2Cuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(InlineThreeFloat2)), s_cudaDialect);
        var inlineFloat2Cpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(InlineThreeFloat2)), s_cpuDialect);
        Assert.True(inlineFloat2Cuda.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<InlineThreeFloat2>(), inlineFloat2Cuda.Host.Size);
        Assert.Equal(24, inlineFloat2Cuda.Device.Size);
        Assert.Equal(8, inlineFloat2Cuda.Device.Alignment);
        Assert.Equal(4, inlineFloat2Cuda.Host.Alignment);
        Assert.Equal(TransferClass.Identical, inlineFloat2Cuda.Transfer);
        Assert.Equal(TransferClass.Identical, inlineFloat2Cpu.Transfer);

        // InlineArray with DivergentInlineElem (Host size=12, Cuda size=16)
        var divInlineCuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(StructWithDivergentInlineArray)), s_cudaDialect);
        var divInlineCpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(nameof(StructWithDivergentInlineArray)), s_cpuDialect);
        Assert.True(divInlineCuda.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<StructWithDivergentInlineArray>(), divInlineCuda.Host.Size);
        Assert.Equal(28, divInlineCuda.Host.Size);
        Assert.Equal(40, divInlineCuda.Device.Size);
        Assert.Equal(TransferClass.Convert, divInlineCuda.Transfer);
        Assert.Equal(TransferClass.Identical, divInlineCpu.Transfer);
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(nameof(WideVectorsFirst))]
    [InlineData(nameof(SystemVectorFirst))]
    public void HostAndDeviceLayout_AlignmentOnlyDifference_IsIdentical(string structName)
    {
        var cuda = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(structName), s_cudaDialect);
        var cpu = TypeLayoutCalculator.GetStructLayout(GetStructSymbol(structName), s_cpuDialect);

        var hostSize = structName == nameof(WideVectorsFirst)
            ? Unsafe.SizeOf<WideVectorsFirst>()
            : Unsafe.SizeOf<SystemVectorFirst>();
        Assert.Equal(hostSize, cuda.Host.Size);
        Assert.Equal(hostSize, cuda.Device.Size);
        Assert.Equal(4, cuda.Host.Alignment);
        Assert.True(cuda.Device.Alignment > cuda.Host.Alignment);
        Assert.Equal(
            cuda.Host.Fields.Select(static f => f.Offset),
            cuda.DeviceFields.Select(static f => f.Offset));
        Assert.Equal(TransferClass.Identical, cuda.Transfer);
        Assert.Equal(TransferClass.Identical, cpu.Transfer);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void HostLayout_ExplicitAutoProperties_ReadTheBackingFieldOffsets()
    {
        var layout = TypeLayoutCalculator.GetStructLayout(
            GetStructSymbol(nameof(ExplicitAutoPropertyStruct)),
            s_cudaDialect);

        var s = new ExplicitAutoPropertyStruct { A = 0x01020304, B = 2.5f };
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref s, 1));
        Assert.Equal(0x01020304, BitConverter.ToInt32(bytes[..4]));
        Assert.Equal(2.5f, BitConverter.ToSingle(bytes[4..8]));

        Assert.True(layout.Host.IsKnown);
        Assert.Equal(Unsafe.SizeOf<ExplicitAutoPropertyStruct>(), layout.Host.Size);
        AssertFieldOffset(layout.Host, nameof(ExplicitAutoPropertyStruct.A), 0);
        AssertFieldOffset(layout.Host, nameof(ExplicitAutoPropertyStruct.B), 4);
        Assert.Equal(4, layout.DeviceFields.First(f => f.Member.Name == nameof(ExplicitAutoPropertyStruct.B)).Offset);
        Assert.Equal(8, layout.Device.Size);
        Assert.Equal(TransferClass.Identical, layout.Transfer);
    }

    // StructLayout.Size (#621): the host size is the CLR's max(Size, end of the last field) without rounding up, checked
    // against the runtime itself; the device size is that rounded up to the alignment, as C++ sizeof is
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
        var genericDouble = GetStructSymbol("SizedGeneric").Construct(compilation.GetSpecialType(SpecialType.System_Double));
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

    // A struct read from metadata has the layout of the same struct read from source, on both dialects. StructLayout
    // and FieldOffset are pseudo-attributes: from metadata, Roslyn returns neither, and only the ClassLayout and
    // FieldLayout tables carry them (#736)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void StructLayout_FromMetadata_MatchesTheSameStructFromSource()
    {
        var (source, _, _) = GetContext();
        var (_, metadataNamespace) = LoadFromMetadata("Layout/HostLayoutCases.cs");
        var sourceNamespace = GetLayoutNamespace(source.Assembly.GlobalNamespace);

        var structs = sourceNamespace.GetTypeMembers()
            .Where(static t => t is { TypeKind: TypeKind.Struct, IsGenericType: false })
            .ToList();
        var mismatches = new List<string>();
        foreach (var sourceStruct in structs)
        {
            var metadataStruct = metadataNamespace.GetTypeMembers(sourceStruct.Name).Single();
            Assert.True(metadataStruct.DeclaringSyntaxReferences.IsEmpty);
            CompareSourceAndMetadataLayouts(sourceStruct, metadataStruct, mismatches);
        }

        Assert.Contains(structs, static s => s.Name == nameof(ExplicitUnionStruct));
        Assert.Contains(structs, static s => s.Name == nameof(SizedPacked));
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    // A constructed generic struct, and a struct nested in a constructed generic type, have no metadata token of their
    // own, nor do their fields: the reader maps the type to the TypeDef row of its definition and a field to the
    // FieldDef row of its definition (#736). Each case also checks the source layout, so that a layout the two paths
    // get wrong the same way does not pass
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Layout/HostLayoutCases.cs", "SizedGeneric", null, "Sequential pack=0 device=TypeLayout { Size = 16, Alignment = 8 }")]
    [InlineData("Layout/GenericLayoutCases.cs", "PackedGeneric", null, "host=True/9/1 [B@0, V@1]")]
    [InlineData("Layout/GenericLayoutCases.cs", "ExplicitGeneric", null, "host=True/16/8 [B@0, V@8]")]
    [InlineData("Layout/GenericLayoutCases.cs", "GenericOuter", "ExplicitInner", "host=True/24/8 [I@4, V@16]")]
    public void StructLayout_FromMetadata_ConstructedGenericMatchesTheSameStructFromSource(
        string testCase, string genericName, string nestedName, string expectedSourceLayout)
    {
        var (sourceCompilation, _, _) = RoslynTestUtility.GetTestCaseContext(testCase);
        var (consumer, metadataNamespace) = LoadFromMetadata(testCase);
        var sourceStruct = ConstructWithDouble(
            sourceCompilation,
            GetLayoutNamespace(sourceCompilation.Assembly.GlobalNamespace),
            genericName,
            nestedName
        );
        var metadataStruct = ConstructWithDouble(consumer, metadataNamespace, genericName, nestedName);

        Assert.True(metadataStruct.DeclaringSyntaxReferences.IsEmpty);
        Assert.False(SymbolEqualityComparer.Default.Equals(metadataStruct, metadataStruct.OriginalDefinition));
        Assert.Contains(expectedSourceLayout, DescribeLayout(TypeLayoutCalculator.GetStructLayout(sourceStruct, s_cudaDialect)));
        var mismatches = new List<string>();
        CompareSourceAndMetadataLayouts(sourceStruct, metadataStruct, mismatches);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));

        static INamedTypeSymbol ConstructWithDouble(
            Compilation compilation, INamespaceSymbol layoutNamespace, string genericName, string nestedName)
        {
            var constructed = layoutNamespace.GetTypeMembers(genericName, 1).Single()
                .Construct(compilation.GetSpecialType(SpecialType.System_Double));
            return nestedName is null ? constructed : constructed.GetTypeMembers(nestedName).Single();
        }
    }

    // Every member hands over the field that stores it, so the offset of an auto-property of an explicit struct is read
    // from its backing field. From source the member is the property; from metadata Roslyn does not link the backing
    // field to it, so the member is that field itself (#853 review)
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void StructAbiMember_StorageField_IsTheFieldThatHoldsTheMember(bool fromMetadata)
    {
        var structType = fromMetadata
            ? LoadFromMetadata("Layout/HostLayoutCases.cs").LayoutNamespace
                .GetTypeMembers(nameof(ExplicitAutoPropertyStruct)).Single()
            : GetStructSymbol(nameof(ExplicitAutoPropertyStruct));

        var members = new List<StructAbiMember>();
        foreach (var member in StructAbiMembers.Get(structType))
        {
            members.Add(member);
        }

        Assert.Equal(2, members.Count);
        foreach (var member in members)
        {
            var storage = Assert.IsAssignableFrom<IFieldSymbol>(member.StorageField);
            Assert.True(
                fromMetadata
                    ? SymbolEqualityComparer.Default.Equals(storage, member.Symbol)
                    : SymbolEqualityComparer.Default.Equals(storage.AssociatedSymbol, member.Symbol),
                $"{member.Name}: {storage.Name} does not hold {member.Symbol.Name}"
            );
        }

        Assert.Equal([0, 4], members.Select(static m => StructLayoutReader.GetFieldOffset(m.StorageField)));
    }

    // A ClassLayout row with a PackingSize above 255, or with a ClassSize above int.MaxValue (System.Reflection.Metadata
    // throws BadImageFormatException on reading it), is malformed. As Roslyn's PEModule.GetTypeLayout does, the reader
    // drops such a value, and falls back to the attributes on a row it cannot read, instead of throwing out of the
    // generator or feeding the value into the layout (#853 review)
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData((ushort)512, 0u)]
    [InlineData((ushort)0, uint.MaxValue)]
    public void StructLayout_FromMalformedClassLayoutRow_DropsAnOutOfRangePackAndSize(ushort packingSize, uint size)
    {
        var library = MetadataReference.CreateFromImage(BuildStructWithClassLayoutRow(packingSize, size));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var structType = consumer.GetTypeByMetadataName("Malformed.BadLayout");

        Assert.NotNull(structType);
        StructLayoutReader.Read(structType, out var kind, out var pack, out var readSize);
        Assert.Equal((TypeLayoutKind.Sequential, 0, 0), (kind, pack, readSize));
    }

    // A library with one sequential struct, Malformed.BadLayout, whose ClassLayout row holds the given values, which the
    // C# compiler never emits
    private static System.Collections.Immutable.ImmutableArray<byte> BuildStructWithClassLayoutRow(ushort packingSize,
        uint size)
    {
        var metadata = new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Malformed.dll"),
            metadata.GetOrAddGuid(new Guid("5f1d8a52-3b0e-4c41-9b0b-0c3f6d2e7a11")),
            default,
            default
        );
        metadata.AddAssembly(
            metadata.GetOrAddString("Malformed"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            System.Reflection.AssemblyHashAlgorithm.None
        );
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(4, 0, 0, 0),
            default,
            default,
            default,
            default
        );
        var valueType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueType")
        );
        var firstField = System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, firstField, firstMethod);
        var type = metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed |
            System.Reflection.TypeAttributes.SequentialLayout,
            metadata.GetOrAddString("Malformed"),
            metadata.GetOrAddString("BadLayout"),
            valueType,
            firstField,
            firstMethod
        );
        metadata.AddTypeLayout(type, packingSize, size);

        var peBuilder = new System.Reflection.PortableExecutable.ManagedPEBuilder(
            System.Reflection.PortableExecutable.PEHeaderBuilder.CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(metadata),
            new System.Reflection.Metadata.BlobBuilder()
        );
        var image = new System.Reflection.Metadata.BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToImmutableArray();
    }

    // The test case compiled into a library, and the Layout namespace of that library as a consumer sees it
    private static (Compilation Consumer, INamespaceSymbol LayoutNamespace) LoadFromMetadata(string testCase)
    {
        var library = DeviceLibraryBuilder.Build("HostLayoutLibrary", null, RoslynTestUtility.ReadTestCase(testCase));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var layoutNamespace = GetLayoutNamespace(consumer.GetAssemblyOrModuleSymbol(library) is IAssemblySymbol assembly
            ? assembly.GlobalNamespace
            : throw new InvalidOperationException("The library is not referenced"));
        return (consumer, layoutNamespace);
    }

    private static INamespaceSymbol GetLayoutNamespace(INamespaceSymbol global) =>
        global.GetNamespaceMembers().Single(static n => n.Name == "Kassyi")
            .GetNamespaceMembers().Single(static n => n.Name == "GSharp")
            .GetNamespaceMembers().Single(static n => n.Name == "Tests")
            .GetNamespaceMembers().Single(static n => n.Name == "Shared")
            .GetNamespaceMembers().Single(static n => n.Name == "TestCases")
            .GetNamespaceMembers().Single(static n => n.Name == "Layout");

    private static void CompareSourceAndMetadataLayouts(
        INamedTypeSymbol sourceStruct, INamedTypeSymbol metadataStruct, List<string> mismatches)
    {
        foreach (var dialect in new[] { s_cudaDialect, s_cpuDialect })
        {
            var expected = DescribeLayout(TypeLayoutCalculator.GetStructLayout(sourceStruct, dialect));
            var actual = DescribeLayout(TypeLayoutCalculator.GetStructLayout(metadataStruct, dialect));
            if (expected != actual)
            {
                mismatches.Add($"{sourceStruct} ({dialect.GetType().Name}):\n  source   {expected}\n  metadata {actual}");
            }
        }
    }

    private static string DescribeLayout(StructLayoutInfo layout) =>
        $"{layout.Kind} pack={layout.Pack} device={layout.Device} " +
        $"[{string.Join(", ", layout.DeviceFields.Select(static f => $"{FieldName(f)}@{f.Offset}"))}] " +
        $"host={layout.Host.IsKnown}/{layout.Host.Size}/{layout.Host.Alignment} " +
        $"[{string.Join(", ", layout.Host.Fields.Select(static f => $"{FieldName(f)}@{f.Offset}"))}] " +
        $"{layout.Transfer} {layout.Unsupported?.Reason}";

    // From metadata, an auto-property is its backing field (<A>k__BackingField); the offsets are what is compared
    private static string FieldName(FieldLayout field) =>
        field.Member.Name is ['<', .. var rest] && rest.EndsWith(">k__BackingField", StringComparison.Ordinal)
            ? rest[..^">k__BackingField".Length]
            : field.Member.Name;
}
