namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct Meter_OpTest
{
    public float Value;
    public static implicit operator Meter_OpTest(float v) => new Meter_OpTest { Value = v };
    public static implicit operator float(Meter_OpTest m) => m.Value;
}

public struct SensorReading_OpTest
{
    public int Id { get; set; }
    public float Temperature { get; set; }
}

public struct Box_OpTest
{
    public int Width;
    public int Area => Width * Width;
}

public interface IConstantProvider_OpTest
{
    static abstract int GetMagic();
}

public struct ConstantImpl_OpTest : IConstantProvider_OpTest
{
    public int Value;
    public static int GetMagic() => 77;
}

public class OperatorsAndInterfacesTarget
{
    public static int GetMagicHelper() => ConstantImpl_OpTest.GetMagic();

    public static void ConversionOperators(float[] output)
    {
        Meter_OpTest m = 42.5f;
        float val = m;
        output[0] = val;
    }

    public static void AutoProperties(SensorReading_OpTest[] readings)
    {
        readings[0] = new SensorReading_OpTest { Id = 42, Temperature = 36.5f };
    }

    public static void ExpressionBodiedProperties(Box_OpTest[] boxes)
    {
        boxes[0] = new Box_OpTest { Width = 5 };
    }

    public static void StaticAbstractMembers(ConstantImpl_OpTest[] items)
    {
        items[0].Value = GetMagicHelper();
    }

    public static void CheckedUnchecked(int[] output, int a, int b)
    {
        int x = checked(a + b);
        unchecked
        {
            output[0] = x * 2;
        }
    }

    public static void ExtensionMethods(int[] data, int[] output)
    {
        Span<int> span = data.AsSpan(1, 4);
        output[0] = span[0];
    }

    public static void NullableValueTypes(int?[] inputs, int[] output)
    {
        int? maybe = inputs[0];
        output[0] = maybe ?? 99;
    }
}

public class OperatorsAndInterfacesTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_ConversionOperators_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.ConversionOperators), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        float[] output = new float[1];
        OperatorsAndInterfacesTarget.ConversionOperators(output);
        Assert.Equal(42.5f, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_AutoProperties_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.AutoProperties), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        var readings = new SensorReading_OpTest[1];
        OperatorsAndInterfacesTarget.AutoProperties(readings);
        Assert.Equal(42, readings[0].Id);
        Assert.Equal(36.5f, readings[0].Temperature);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_ExpressionBodiedProperties_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.ExpressionBodiedProperties), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        var boxes = new Box_OpTest[1];
        OperatorsAndInterfacesTarget.ExpressionBodiedProperties(boxes);
        Assert.Equal(5, boxes[0].Width);
        Assert.Equal(25, boxes[0].Area);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_StaticAbstractMembers_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.StaticAbstractMembers), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        var items = new ConstantImpl_OpTest[1];
        OperatorsAndInterfacesTarget.StaticAbstractMembers(items);
        Assert.Equal(77, items[0].Value);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_CheckedUnchecked_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.CheckedUnchecked), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the construct (UnsupportedSyntaxKindDiagnosticsTests); the transpiler records the failure and
        // writes a placeholder
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, "'CheckedExpression'");
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, "'UncheckedStatement'");
        Assert.Contains("/*unsupported:CheckedExpression*/", cudaSource);
        Assert.Contains("/*unsupported:UncheckedStatement*/", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_ExtensionMethods_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.ExtensionMethods), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        int[] data = [10, 20, 30, 40, 50];
        int[] output = new int[1];
        OperatorsAndInterfacesTarget.ExtensionMethods(data, output);
        Assert.Equal(20, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_NullableValueTypes_Execution()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<OperatorsAndInterfacesTarget>(
            nameof(OperatorsAndInterfacesTarget.NullableValueTypes), isDevice: false, dialect: CurrentDialect);
        Assert.Empty(diagnostics);

        int[] output = new int[1];
        OperatorsAndInterfacesTarget.NullableValueTypes([null], output);
        Assert.Equal(99, output[0]);

        OperatorsAndInterfacesTarget.NullableValueTypes([42], output);
        Assert.Equal(42, output[0]);
    }
}
