using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class AdvancedMemoryTests : TranspilerTestBase
{
    // fixed on a [GpuReadOnly] array declares a pointer to const elements; before, the pointer was declared without
    // const and the generated C++ did not compile
    // mutant: VariableDeclarationTranspilerRule.cs PinsReadOnlyArray returning false -> the test project does not build
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void ReadOnlyPin_PinnedReadOnlyArrays_ReadAndCompareTheirAddresses(bool sameArray, uint expectedSame)
    {
        uint[] src = [7, 5];
        uint[] other = sameArray ? src : [7, 5];
        uint[] dest = [0, 0];

        AdvancedMemoryKernels.LaunchReadOnlyPinKernel(src, other, dest);

        Assert.Equal(12u, dest[0]);
        Assert.Equal(expectedSame, dest[1]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void PointerIncrement_SequentialArray_CopiesElementsToDestination()
    {
        uint[] src = [10, 20, 30];
        uint[] dest = [0, 0, 0];

        AdvancedMemoryKernels.LaunchPointerIncrementKernel(src, dest);

        Assert.Equal(10u, dest[0]);
        Assert.Equal(20u, dest[1]);
        Assert.Equal(30u, dest[2]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void PointerDecrement_SequentialArray_CopiesElementsToDestination()
    {
        uint[] src = [100, 200, 300];
        uint[] dest = [0, 0, 0];

        AdvancedMemoryKernels.LaunchPointerDecrementKernel(src, dest);

        Assert.Equal(100u, dest[0]);
        Assert.Equal(200u, dest[1]);
        Assert.Equal(300u, dest[2]);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(new uint[] { 2, 0, 1 }, new uint[] { 5, 15, 25 }, new uint[] { 25, 35, 15 })]
    [InlineData(new uint[] { 0, 1, 2 }, new uint[] { 1, 2, 3 }, new uint[] { 11, 12, 13 })]
    [InlineData(new uint[] { 0, 1, 2 }, new uint[] { 0, 1, uint.MaxValue }, new uint[] { 10, 11, 9 })]
    [InlineData(new uint[] { 1, 2, 0 }, new uint[] { 0x55555555u, 0xAAAAAAAAu, 0x12345678u }, new uint[] { 0x12345682u, 0x5555555Fu, 0xAAAAAAB4u })]
    [InlineData(new uint[] { 2, 1, 0 }, new uint[] { 0, 0, 0 }, new uint[] { 10, 10, 10 })]
    public void PointerComplexAssignment_PermutedIndicesAndValues_ProducesExpectedDestination(
        uint[] idx,
        uint[] val,
        uint[] expectedDest)
    {
        uint[] dest = [0, 0, 0];

        AdvancedMemoryKernels.LaunchPointerComplexAssignmentKernel(idx, val, dest);

        Assert.Equal(expectedDest[0], dest[0]);
        Assert.Equal(expectedDest[1], dest[1]);
        Assert.Equal(expectedDest[2], dest[2]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_UnsafeAsPointer_FailsAsAMethodTheDialectDoesNotImplement()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<AdvancedMemoryTarget>(
            nameof(AdvancedMemoryTarget.UnsafeAsPointer), dialect: CurrentDialect);

        // No dialect defines Unsafe.AsPointer; its bare name would not build
        diagnostics.AssertFails(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode, "'Unsafe.AsPointer");
        Assert.DoesNotContain(")AsPointer(", cudaSource.WithCSharpNames());
        Assert.Contains("*ptr=777;", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_MemoryMarshalCast_FailsAsAMethodTheDialectDoesNotImplement()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<AdvancedMemoryTarget>(
            nameof(AdvancedMemoryTarget.MemoryMarshalCast), dialect: CurrentDialect);

        // No dialect defines MemoryMarshal.Cast; its bare name would not build
        diagnostics.AssertFails(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode, "'MemoryMarshal.Cast(");
        Assert.DoesNotContain("= Cast(", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_RefFieldsUpdate_EmitsRefWrapperStructAndAssignment()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<AdvancedMemoryTarget>(
            nameof(AdvancedMemoryTarget.RefFieldsUpdate), isDevice: true, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("struct Kassyi.GSharp.Tests.Shared.Syntax.RefWrapper", cudaSource.WithCSharpNames());
        Assert.Contains("wrapper.Value=123;", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_SimdVector128_EmitsVectorTypeAndArrayAccess()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<AdvancedMemoryTarget>(
            nameof(AdvancedMemoryTarget.SimdVector128), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        if (IsCudaDialect)
        {
            // The target is not a [GpuKernel], so its array parameter may be null and each read is checked (#706)
            Assert.Matches(@"float4 v = __gsharp_array_at\(vectors, 0, ""[^""]*""\);", cudaSource.WithCSharpNames());
        }
        else
        {
            Assert.Matches(@"Kassyi\.GSharp\.Core\.Float4 v = __gsharp_array_at\(vectors, 0, ""[^""]*""\);", cudaSource.WithCSharpNames());
        }
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_PointerIncrementKernel_EmitsPointerArithmeticAssignments()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod(
            typeof(AdvancedMemoryKernels), nameof(AdvancedMemoryKernels.PointerIncrementKernel), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("ptrS = pSrc;", cudaSource.WithCSharpNames());
        Assert.Contains("*ptrD++=*ptrS++;", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_FixedSizeBuffers_EmitsStructWithInlineArray()
    {
        var (cudaSource, diagnostics) = TranspileTestRunner.TranspileMethod<AdvancedMemoryTarget>(
            nameof(AdvancedMemoryTarget.FixedSizeBuffers), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("struct Kassyi.GSharp.Tests.Shared.Syntax.FixedBufferStruct_AdvMem", cudaSource.WithCSharpNames());
        Assert.Contains("Data[16];", cudaSource.WithCSharpNames());
    }
}

public ref struct RefWrapper
{
    public ref int Value;
}

public unsafe struct FixedBufferStruct_AdvMem
{
    public fixed int Data[16];
}

public class AdvancedMemoryTarget
{
    public static unsafe void UnsafeAsPointer(int[] data)
    {
        int* ptr = (int*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref data[0]);
        *ptr = 777;
    }

    public static void MemoryMarshalCast(byte[] bytes, int[] output)
    {
        Span<int> ints = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
        output[0] = ints[0];
    }

    public static void RefFieldsUpdate(RefWrapper wrapper)
    {
        wrapper.Value = 123;
    }

    public static void SimdVector128(Float4[] vectors, float[] output)
    {
        Float4 v = vectors[0];
        output[0] = v.X + v.Y + v.Z + v.W;
    }

    public static unsafe void FixedSizeBuffers(FixedBufferStruct_AdvMem[] items, int[] output)
    {
        FixedBufferStruct_AdvMem item = items[0];
        output[0] = item.Data[0];
    }
}


