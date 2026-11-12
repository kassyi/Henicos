using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct Point2D_ModernPattern
{
    public int X;
    public int Y;
}

public class ModernPatternsTarget
{
    public static void ListPatterns(int[] data, int[] output)
    {
        output[0] = data switch
        {
            [1, 2, ..] => 10,
            _ => 0
        };
    }

    public static void PropertyPatterns(Point2D_ModernPattern[] points, int[] output)
    {
        Point2D_ModernPattern p = points[0];
        output[0] = p switch
        {
            { X: 0, Y: 0 } => 1,
            _ => 0
        };
    }

    public static void PositionalPatterns(int[] a, int[] b, int[] output)
    {
        output[0] = (a[0], b[0]) switch
        {
            (0, 0) => 1,
            _ => 2
        };
    }

    public static void LambdaDefaultParameters(int[] output)
    {
        var addWithDefault = (int x, int step = 5) => x + step;
        output[0] = addWithDefault(10, 5);
    }

    public static void LambdaExplicitReturnType(int[] output)
    {
        Func<int, int> scale = int (int x) => x * 3;
        output[0] = scale(7);
    }

    public static int ScopedRef(scoped ref int value, scoped ReadOnlySpan<int> buffer)
    {
        return value + buffer[0];
    }

    public static void UnsafeNullRef(int[] output)
    {
        bool isNull = Unsafe.IsNullRef(ref output[0]);
        output[0] = isNull ? 1 : 0;
    }
}

public class ModernPatternsTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_ListPatterns_FailsWithUnsupportedSyntaxDiagnostic()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.ListPatterns), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the pattern (UnsupportedConstructDiagnosticsTests); the transpiler records the failure
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, GpuCodeConstraints.SwitchArmPatternNotSupported);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_PropertyPatterns_FailsWithUnsupportedSyntaxDiagnostic()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.PropertyPatterns), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the pattern (UnsupportedConstructDiagnosticsTests); the transpiler records the failure
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, GpuCodeConstraints.SwitchArmPatternNotSupported);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_PositionalPatterns_FailsWithUnsupportedSyntaxAndEmitsPlaceholder()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.PositionalPatterns), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the construct (UnsupportedSyntaxKindDiagnosticsTests); the transpiler records the failure and
        // writes a placeholder
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, "'TupleExpression'");
        Assert.Contains("/*unsupported:TupleExpression*/", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_LambdaDefaultParameters_EmitsValidSourceAndCalculatesExpectedValue()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.LambdaDefaultParameters), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] output = new int[1];
        ModernPatternsTarget.LambdaDefaultParameters(output);
        Assert.Equal(15, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_LambdaExplicitReturnType_EmitsValidSourceAndCalculatesExpectedValue()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.LambdaExplicitReturnType), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int[] output = new int[1];
        ModernPatternsTarget.LambdaExplicitReturnType(output);
        Assert.Equal(21, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_ScopedRef_EmitsValidSourceAndCalculatesExpectedValue()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.ScopedRef), isDevice: true, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        int val = 10;
        int result = ModernPatternsTarget.ScopedRef(ref val, [20]);
        Assert.Equal(30, result);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Transpile_UnsafeNullRef_FailsOnTheDeviceAndEvaluatesOnTheHost()
    {
        var (_, diagnostics) = RoslynTestUtility.TranspileMethod<ModernPatternsTarget>(
            nameof(ModernPatternsTarget.UnsafeNullRef), isDevice: false, dialect: CurrentDialect);

        // No dialect defines Unsafe.IsNullRef; its bare name would not build
        diagnostics.AssertFails(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode, "'Unsafe.IsNullRef(");

        int[] output = [42];
        ModernPatternsTarget.UnsafeNullRef(output);
        Assert.Equal(0, output[0]);
    }
}
