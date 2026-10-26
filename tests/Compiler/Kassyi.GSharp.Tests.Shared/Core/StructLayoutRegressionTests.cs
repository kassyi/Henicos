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

    [Fact(Skip = "L4")]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void R4a_StructWithArrayField_ByValue_PreservesScalarAndArrayElements()
    {
        var o = new int[3];
        var b = new LayoutReproBag { N = 3, Data = [10, 20, 30] };

        StructLayoutRegressionKernels.LaunchBagByValKernel(b, o);

        Assert.Equal([3, 3, 20], o);
    }

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

    [Fact(Skip = "L4")]
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
