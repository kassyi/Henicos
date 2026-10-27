using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class StructLayoutRegressionTests
{
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R1_VectorStructArray_PreservesFieldOffsetsAndWritesBack()
    {
        var h = new LayoutReproVecHolder[2];
        h[0] = new LayoutReproVecHolder
        {
            A = 1f,
            V = new Vector2(2f, 3f),
            B = 4f,
            W = new Vector4(5f, 6f, 7f, 8f)
        };
        h[1] = new LayoutReproVecHolder { A = 11f };
        var o = new float[5];

        StructLayoutRegressionKernels.LaunchVecArrayKernel(h, o);

        Assert.Equal(3f, o[0]);
        Assert.Equal(7f, o[1]);
        Assert.Equal(11f, o[2]);
        Assert.Equal(42f, h[0].B);
        Assert.Equal(new Vector4(5f, 6f, 7f, 8f), h[0].W);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R2_CoreVectorStructArray_PreservesFieldOffsets()
    {
        var h = new LayoutReproCoreHolder[2];
        h[0] = new LayoutReproCoreHolder
        {
            A = 1f,
            V = new Float2(2f, 3f),
            B = 4f,
            W = new Float4(5f, 6f, 7f, 8f)
        };
        h[1] = new LayoutReproCoreHolder { A = 11f };
        var o = new float[5];

        StructLayoutRegressionKernels.LaunchCoreArrayKernel(h, o);

        Assert.Equal(3f, o[0]);
        Assert.Equal(7f, o[1]);
        Assert.Equal(11f, o[2]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R3_InlineArrayWithDivergentElementLayout_RoundtripsElements()
    {
        var x = new LayoutReproInlineOuter { Tag = 5, C = 7 };
        x.Items[0] = new LayoutReproElem { A = 1, V = new Float2(2f, 3f) };
        x.Items[1] = new LayoutReproElem { A = 4, V = new Float2(5f, 6f) };
        var o = new float[5];

        StructLayoutRegressionKernels.LaunchInlineElemKernel(ref x, o);

        Assert.Equal(6f, o[0]);
        Assert.Equal(4f, o[1]);
        Assert.Equal(7f, o[2]);
        Assert.Equal(8, x.C);
        Assert.Equal(4, x.Items[1].A);
        Assert.Equal(new Float2(9f, 10f), x.Items[1].V);
    }

#if GSHARP_CPU
    // The CPU launcher does not convert a struct whose layouts differ yet (#511): it stops before the native call
    // instead of passing the host layout (GSH0082 reports it at compile time where analyzers run)
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void R4a_StructWithArrayField_ByValue_ThrowsNotSupportedOnCpu()
    {
        var o = new int[] { -1, -1, -1 };
        var b = new LayoutReproBag { N = 3, Data = [10, 20, 30] };

        var exception = Assert.Throws<System.NotSupportedException>(
            () => StructLayoutRegressionKernels.LaunchBagByValKernel(b, o));

        Assert.Contains("'Kassyi.GSharp.Tests.Shared.Core.LayoutReproBag'", exception.Message);
        Assert.Contains("field 'Data' is an array", exception.Message);
        Assert.Equal([-1, -1, -1], o);
    }
#else
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R4a_StructWithArrayField_ByValue_PreservesScalarAndArrayElements()
    {
        var o = new int[3];
        var b = new LayoutReproBag { N = 3, Data = [10, 20, 30] };

        StructLayoutRegressionKernels.LaunchBagByValKernel(b, o);

        Assert.Equal([3, 3, 20], o);
    }
#endif

#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R4c_StructWithArrayField_Array_PreservesElements()
    {
        var o = new int[3];
        var b = new LayoutReproBag[]
        {
            new() { N = 3, Data = [10, 20, 30] },
            new() { N = 4, Data = [1] }
        };

        StructLayoutRegressionKernels.LaunchBagArrayKernel(b, o);

        Assert.Equal([3, 3, 4], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5a_StructWithMultipleBools_ByValue_PreservesSubsequentFields()
    {
        var o = new int[3];
        var t = new LayoutReproTwoBools { F = true, G = true, X = 77 };

        StructLayoutRegressionKernels.LaunchTwoBoolsByValKernel(t, o);

        Assert.Equal([1, 1, 77], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5b_StructWithChar_ByValue_PreservesTheCharAndTheNextField()
    {
        var o = new int[2];
        var c = new LayoutReproCharInt { C = (char)0x3042, X = 77 };

        StructLayoutRegressionKernels.LaunchCharIntByValKernel(c, o);

        Assert.Equal([0x3042, 77], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5c_GenericStruct_ByValue_PreservesFields()
    {
        var o = new double[2];
        var b = new LayoutReproBox<double> { V = 2.5, Tag = 7 };

        StructLayoutRegressionKernels.LaunchGenericBoxByValKernel(b, o);

        Assert.Equal([2.5, 7.0], o);
    }

    [Theory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(5, 5)]
    [InlineData(null, -1)]
    public void R5d_StructWithNullableField_ByValue_PreservesValueAndNextField(int? value, int expected)
    {
        var o = new int[2];
        var n = new LayoutReproNullableInt { V = value, X = 77 };

        StructLayoutRegressionKernels.LaunchNullableFieldByValKernel(n, o);

        Assert.Equal([expected, 77], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5f_StructWithMultipleBools_ByRefReadOnly_PreservesSubsequentFields()
    {
        var o = new int[3];
        var t = new LayoutReproTwoBools { F = false, G = true, X = 77 };

        StructLayoutRegressionKernels.LaunchTwoBoolsByRefReadOnlyKernel(t, o);

        Assert.Equal([0, 1, 77], o);
    }

    // Each of 2 x 4 threads changes its own copy of the value parameter; the host value does not change
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void R5g_ValueStructParameter_WrittenByEachThread_IsAPerThreadCopy()
    {
        var o = new int[8];
        var t = new LayoutReproTwoBools { F = true, G = false, X = 100 };

        StructLayoutRegressionKernels.LaunchMutateByValKernel(t, o, gridDimX: 2, blockDimX: 4);

        Assert.Equal([100, 101, 102, 103, 104, 105, 106, 107], o);
        Assert.Equal(100, t.X);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void R5h_PackedStruct_ByValue_PreservesUnalignedFields()
    {
        var o = new int[3];
        var p = new LayoutReproPacked { B = 9, I = 0x01020304, F = true };

        StructLayoutRegressionKernels.LaunchPackedByValKernel(p, o);

        Assert.Equal([9, 0x01020304, 1], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5i_NestedBoolStructAndChar_ByValue_PreservesFields()
    {
        var o = new int[4];
        var n = new LayoutReproNested
        {
            T = new LayoutReproTwoBools { F = true, G = false, X = 77 },
            C = (char)0x3042,
            Y = 88
        };

        StructLayoutRegressionKernels.LaunchNestedByValKernel(n, o);

        Assert.Equal([1, 77, 0x3042, 88], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R5e_StructWithMultipleBools_ByIn_PreservesSubsequentFields()
    {
        var o = new int[3];
        var t = new LayoutReproTwoBools { F = true, G = false, X = 77 };

        StructLayoutRegressionKernels.LaunchTwoBoolsByInKernel(t, o);

        Assert.Equal([1, 0, 77], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R6a_ExplicitLayoutUnion_ByValue_HasExpectedBitPatternAndDeviceSize()
    {
        var o = new int[2];
        var u = new LayoutReproIntFloatUnion { F = 1.0f };

        StructLayoutRegressionKernels.LaunchUnionKernel(u, o);

        Assert.Equal([1065353216, 4], o);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R6b_ExplicitLayoutUnion_Array_PreservesElementStride()
    {
        var o = new int[1];
        var u = new LayoutReproIntFloatUnion[] { default, new() { F = 1.0f } };

        StructLayoutRegressionKernels.LaunchUnionArrayKernel(u, o);

        Assert.Equal(1065353216, o[0]);
    }

#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R7_AutoLayoutStruct_Array_PreservesFieldValues()
    {
        var o = new int[4];
        var a = new LayoutReproAutoLayout[]
        {
            new() { A = 1, B = 2, C = 3 },
            new() { A = 4, B = 5, C = 6 }
        };

        StructLayoutRegressionKernels.LaunchAutoArrayKernel(a, o);

        Assert.Equal([1, 2, 3, 4], o);
    }
}

[Collection(TestCollections.GlobalStateSequential)]
public class StructLayoutRegressionSequentialTests
{
#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R4b_StructWithArrayField_ByRef_PreservesFieldsAndWritesBackArrayElement()
    {
        var o = new int[3];
        var b = new LayoutReproBag { N = 3, Data = [10, 20, 30] };

        StructLayoutRegressionKernels.LaunchBagByRefKernel(ref b, o);

        Assert.Equal([3, 3, 20], o);
        Assert.Equal(99, b.Data[1]);
    }

#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R9_InlineArrayWithArrayFieldInElement_ByRef_ReadsElementArray()
    {
        var o = new int[1];
        var x = new LayoutReproArrOuter();
        x.P[0] = new LayoutReproWithArr { Data = [1] };
        x.P[1] = new LayoutReproWithArr { Data = [55] };

        StructLayoutRegressionKernels.LaunchInlineArrFieldKernel(ref x, o);

        Assert.Equal(55, o[0]);
    }
}
