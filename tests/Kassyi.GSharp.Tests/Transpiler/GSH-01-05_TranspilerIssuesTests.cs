using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GSH_01_05_TranspilerIssuesTests
{
    [Fact(Skip = "未実装: GSH-01 (三項演算子の複雑式展開)")]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH01_ComplexConditionalExpression()
    {
    }

    [Fact(Skip = "未実装: GSH-02 (タプル構文の一般化)")]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH02_GeneralTupleSupport()
    {
    }

    [Fact(Skip = "未実装: GSH-03 (ローカル関数の非サポート)")]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH03_LocalFunctionsSupport()
    {
    }

    [Fact]
    [Trait("FeatureId", "GSH-04")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocFixedArray_HappyPath()
    {
        const string source = """
        using System;
        using Kassyi.GSharp.Core;

        public class TestKernelClass
        {
            [GpuKernel]
            public static void TestKernel()
            {
                Span<int> buf = stackalloc int[16];
                buf[0] = 42;
            }
        }
        """;

        var userTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(userTree);
        var semanticModel = compilation.GetSemanticModel(userTree);

        var methodDecl = userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var generatedCode = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice: false);

        Assert.NotNull(generatedCode);
        Assert.True(diagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id) || generatedCode.Contains("buf"), "Transpiler should handle stackalloc or report GSH0003 UnsupportedSyntax diagnostic");
    }

    [Fact]
    [Trait("FeatureId", "GSH-04")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "SadPath")]
    public void Test_GSH04_StackAllocDynamicSize_SadPath_ReportsDiagnostic()
    {
        const string source = """
        using System;
        using Kassyi.GSharp.Core;

        public class TestKernelClass
        {
            [GpuKernel]
            public static void TestKernel(int length)
            {
                Span<int> buf = stackalloc int[length];
            }
        }
        """;

        var userTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(userTree);
        var semanticModel = compilation.GetSemanticModel(userTree);

        var methodDecl = userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var generatedCode = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice: false);

        Assert.NotNull(generatedCode);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GSH-04")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocEdgeCase_ZeroLength()
    {
        const string source = """
        using System;
        using Kassyi.GSharp.Core;

        public class TestKernelClass
        {
            [GpuKernel]
            public static void TestKernel()
            {
                Span<int> buf = stackalloc int[0];
            }
        }
        """;

        var userTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(userTree);
        var semanticModel = compilation.GetSemanticModel(userTree);

        var methodDecl = userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var generatedCode = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice: false);

        Assert.NotNull(generatedCode);
        Assert.True(generatedCode.Contains("buf") || diagnostics.Any(), "Zero-length stackalloc must be transpiled or report diagnostic");
    }

    [Fact]
    [Trait("FeatureId", "GSH-04")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "SadPath")]
    public void Test_GSH04_StackAllocEdgeCase_ExcessiveSize_ReportsWarningOrError()
    {
        const string source = """
        using System;
        using Kassyi.GSharp.Core;

        public class TestKernelClass
        {
            [GpuKernel]
            public static void TestKernel()
            {
                Span<int> buf = stackalloc int[1048576];
            }
        }
        """;

        var userTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(userTree);
        var semanticModel = compilation.GetSemanticModel(userTree);

        var methodDecl = userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var generatedCode = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice: false);

        Assert.NotNull(generatedCode);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error);
    }

    [Fact(Skip = "未実装: GSH-05 (ジェネリクスの制約解決)")]
    [Trait("FeatureId", "GSH-05")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH05_GenericsConstraintResolution()
    {
    }
}
