using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct PrimaryCoord_TypesTest(int x, int y)
{
    public int X = x;
    public int Y = y;
}

public record struct RecordVec_TypesTest(int X, int Y);

[InlineArray(4)]
public struct InlineVec4_TypesTest
{
    private float _element0;
}

public static class MacroOps_TypesTest
{
    [GpuDeviceFunction]
    public static void AddVec(ref CustomVec_TypesTest res, CustomVec_TypesTest a, CustomVec_TypesTest b)
    {
        res.X = a.X + b.X;
    }
}

public struct CustomVec_TypesTest
{
    public int X;

    [GpuOperatorMacro(typeof(MacroOps_TypesTest), nameof(MacroOps_TypesTest.AddVec))]
    public static CustomVec_TypesTest operator +(CustomVec_TypesTest a, CustomVec_TypesTest b)
    {
        CustomVec_TypesTest res = default;
        MacroOps_TypesTest.AddVec(ref res, a, b);
        return res;
    }
}

public readonly struct ImmutablePair_TypesTest(int a, int b)
{
    public readonly int A = a;
    public readonly int B = b;
}

[Flags]
public enum ComputeMode_TypesTest : uint
{
    None = 0,
    Fast = 1,
    Accurate = 2
}

public class TypesAndStructsTarget
{
    public static void TupleDeconstruction(int[] data)
    {
        int a = data[0];
        int b = data[1];
        (a, b) = (b + 1, a + 2);
        data[0] = a;
        data[1] = b;
    }

    public static void PrimaryConstructor(PrimaryCoord_TypesTest[] coords, int[] output)
    {
        PrimaryCoord_TypesTest c = coords[0];
        output[0] = c.X + c.Y;
    }

    public static void RecordStructWithExpression(RecordVec_TypesTest[] items)
    {
        RecordVec_TypesTest v1 = items[0];
        RecordVec_TypesTest v2 = v1 with { X = 99 };
        items[0] = v2;
    }

    public static void StructIndexer(float[] output)
    {
        InlineVec4_TypesTest v = new InlineVec4_TypesTest();
        v[0] = 1.5f;
        v[3] = 4.5f;
        output[0] = v[0] + v[3];
    }

    public static void StructOperatorOverload(CustomVec_TypesTest[] items)
    {
        CustomVec_TypesTest a = items[0];
        CustomVec_TypesTest b = items[1];
        CustomVec_TypesTest c = default;
        c = a + b;
        items[0] = c;
    }

    public static void ReadonlyStruct(ImmutablePair_TypesTest[] pairs, int[] output)
    {
        ImmutablePair_TypesTest p = pairs[0];
        output[0] = p.A + p.B;
    }

    public static void EnumBaseTypeAndFlags(ComputeMode_TypesTest[] modes, uint[] output)
    {
        ComputeMode_TypesTest m = modes[0];
        if (m == ComputeMode_TypesTest.Fast)
        {
            output[0] = 1u;
        }
    }
}

public class TypesAndStructsTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_TargetTypedNew_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod(
            typeof(TargetTypedNewKernels), nameof(TargetTypedNewKernels.HappyPathKernel), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_TupleDeconstruction_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.TupleDeconstruction), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] data = [10, 20];
        TypesAndStructsTarget.TupleDeconstruction(data);
        Assert.Equal(21, data[0]);
        Assert.Equal(12, data[1]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_PrimaryConstructor_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.PrimaryConstructor), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        PrimaryCoord_TypesTest[] coords = [new PrimaryCoord_TypesTest(3, 7)];
        int[] output = new int[1];
        TypesAndStructsTarget.PrimaryConstructor(coords, output);
        Assert.Equal(10, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_RecordStructWithExpression_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.RecordStructWithExpression), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the construct (UnsupportedSyntaxKindDiagnosticsTests); the transpiler writes a placeholder
        Assert.Empty(diagnostics);
        Assert.Contains("/*unsupported:WithExpression*/", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_StructIndexer_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.StructIndexer), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        float[] output = new float[1];
        TypesAndStructsTarget.StructIndexer(output);
        Assert.Equal(6.0f, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_StructOperatorOverload_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.StructOperatorOverload), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        CustomVec_TypesTest[] items = [new() { X = 5 }, new() { X = 15 }];
        TypesAndStructsTarget.StructOperatorOverload(items);
        Assert.Equal(20, items[0].X);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_ReadonlyStruct_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.ReadonlyStruct), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        ImmutablePair_TypesTest[] pairs = [new ImmutablePair_TypesTest(4, 8)];
        int[] output = new int[1];
        TypesAndStructsTarget.ReadonlyStruct(pairs, output);
        Assert.Equal(12, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_EnumBaseTypeAndFlags_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TypesAndStructsTarget>(
            nameof(TypesAndStructsTarget.EnumBaseTypeAndFlags), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        uint[] output = new uint[1];
        TypesAndStructsTarget.EnumBaseTypeAndFlags([ComputeMode_TypesTest.Fast], output);
        Assert.Equal(1u, output[0]);
    }
}

