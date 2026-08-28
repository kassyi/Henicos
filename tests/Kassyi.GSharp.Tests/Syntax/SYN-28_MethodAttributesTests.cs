using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_28_MethodAttributesTests
{
    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileDeviceMethod(string methodName)
    {
        var testCasePath = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Analyzers", "SYN-28_MethodAttributes.cs");
        var code = File.ReadAllText(testCasePath);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var root = syntaxTree.GetRoot();
        var methodDecl = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: true);
        return (cudaSource, diagnostics);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_AggressiveInlining_Execution()
    {
        int[] input = [0, 5, 10, 20];
        int[] result = new int[4];

        SYN_28_MethodAttributesKernelsWrapper.Launch_MethodAttributesKernel(input, result, blockDimX: 4);

        // input 0: (0 + 100) * 2 = 200
        Assert.Equal(200, result[0]);
        // input 5: (5 + 100) * 2 = 210
        Assert.Equal(210, result[1]);
        // input 10: (10 + 100) * 2 = 220
        Assert.Equal(220, result[2]);
        // input 20: (20 + 100) * 2 = 240
        Assert.Equal(240, result[3]);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_AggressiveInlining_TranspilesWithForceinlineDevice()
    {
        var (cudaSource, diagnostics) = TranspileDeviceMethod("AggressiveInlinedFunc");

        Assert.Empty(diagnostics);
        Assert.Contains("__forceinline__ __device__", cudaSource);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_NoInlining_TranspilesWithNoinlineDevice()
    {
        var (cudaSource, diagnostics) = TranspileDeviceMethod("NoInlinedFunc");

        Assert.Empty(diagnostics);
        Assert.Contains("__noinline__ __device__", cudaSource);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_MultipleAttributes_EmitsForceinlineOnceWithoutDuplicates()
    {
        var (cudaSource, diagnostics) = TranspileDeviceMethod("MultipleAttributesFunc");

        Assert.Empty(diagnostics);
        Assert.Contains("__forceinline__ __device__", cudaSource);

        var matches = Regex.Matches(cudaSource, @"__forceinline__");
        Assert.Single(matches);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-28")]
    public void Test_SYN28_UnsupportedOption_NoOptimization_DefaultsToInlineDeviceWithoutError()
    {
        var (cudaSource, diagnostics) = TranspileDeviceMethod("NoOptimizationFunc");

        Assert.Empty(diagnostics);
        Assert.Contains("inline __device__", cudaSource);
        Assert.DoesNotContain("__forceinline__", cudaSource);
        Assert.DoesNotContain("__noinline__", cudaSource);
    }
}
