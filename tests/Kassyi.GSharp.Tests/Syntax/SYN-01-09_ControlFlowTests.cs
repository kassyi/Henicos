
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_01_09_ControlFlowTests
{
    [Fact]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "Syntax")]
    public void Test_SYN01_SwitchExpression_HappyPath_ConstantPatterns()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN01_1
        {
            [GpuKernel]
            public static void Kernel(int state, int[] outVal)
            {
                outVal[0] = state switch
                {
                    1 => 10,
                    2 => 20,
                    _ => 0
                };
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.NotNull(cuda);
    }

    [Fact]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "SadPath")]
    public void Test_SYN01_SwitchExpression_SadPath_NonExhaustive()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN01_2
        {
            [GpuKernel]
            public static void Kernel(int state, int[] outVal)
            {
                outVal[0] = state switch
                {
                    1 => 10,
                    2 => 20
                };
            }
        }
        """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var model = compilation.GetSemanticModel(syntaxTree);
        var methodDecl = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        _ = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        // Verify compiler or transpiler diagnostics detect non-exhaustive switch or unsupported syntax
        var compDiagnostics = compilation.GetDiagnostics();
        Assert.True(compDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error) || diagnostics.Any());
    }

    [Fact]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "Syntax")]
    public void Test_SYN01_SwitchExpression_EdgeCase_SingleExpressionAndRefReturn()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN01_3
        {
            [GpuKernel]
            public static void Kernel(int key, ref int val1, ref int val2, ref int defaultVal, int[] outVal)
            {
                ref int target = ref key switch
                {
                    1 => ref val1,
                    2 => ref val2,
                    _ => ref defaultVal
                };
                target = 100;
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.NotNull(cuda);
    }

    [Fact(Skip = "未実装: SYN-02 (パターンマッチング is / 結合 / リレーショナル)")]
    [Trait("FeatureId", "SYN-02")]
    [Trait("Category", "Syntax")]
    public void Test_SYN02_PatternMatching_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-03 (インライン out var 変数宣言)")]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "Syntax")]
    public void Test_SYN03_InlineOutVarDeclaration_Transpilation()
    {
    }

    [Fact]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "Syntax")]
    public void Test_SYN04_NullCoalescing_HappyPath_NullableStruct()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN04_1
        {
            [GpuKernel]
            public static void Kernel(int? optVal, int fallback, int[] outVal)
            {
                outVal[0] = optVal ?? fallback;
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.NotNull(cuda);
    }

    [Fact]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "SadPath")]
    public void Test_SYN04_NullCoalescing_SadPath_ReferenceType()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass
        {
            [GpuKernel]
            public static void Kernel(string str1, string str2)
            {
                string res = str1 ?? str2;
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        _ = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.StringNotSupported.Id || d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "Syntax")]
    public void Test_SYN04_NullCoalescing_EdgeCase_CompoundAssignmentSideEffects()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN04_3
        {
            [GpuKernel]
            public static void Kernel(int?[] arr, int index, int fallback)
            {
                arr[index] ??= fallback;
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.NotNull(cuda);
    }

    [Fact]
    [Trait("FeatureId", "SYN-05")]
    [Trait("Category", "Syntax")]
    public void Test_SYN05_ConditionalExpression_HappyPath_SimpleAndImplicitCast()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN05_1
        {
            [GpuKernel]
            public static void Kernel(int a, double b, double[] result)
            {
                result[0] = a > 0 ? a : b;
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model);

        Assert.Contains("Kernel", cuda);
        Assert.NotNull(cuda);
    }

    [Fact]
    [Trait("FeatureId", "SYN-05")]
    [Trait("Category", "SadPath")]
    public void Test_SYN05_ConditionalExpression_SadPath_UnsupportedReferenceType()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_SYN05_2
        {
            [GpuKernel]
            public static void Kernel(bool flag)
            {
                string res = flag ? "yes" : "no";
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        _ = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.StringNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }

    [Fact(Skip = "未実装: SYN-06 (ref 条件式 / ref ternary)")]
    [Trait("FeatureId", "SYN-06")]
    [Trait("Category", "Syntax")]
    public void Test_SYN06_RefConditionalExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-07 (ローカル関数 / local functions)")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "Syntax")]
    public void Test_SYN07_LocalFunctions_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-08 (ラムダ式・無名関数 / Lambdas / Anonymous Functions)")]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "Syntax")]
    public void Test_SYN08_LambdaExpressions_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-09 (デフォルトリテラル default, default(T))")]
    [Trait("FeatureId", "SYN-09")]
    [Trait("Category", "Syntax")]
    public void Test_SYN09_DefaultLiteral_Transpilation()
    {
    }
}
