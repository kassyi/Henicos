using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_07_08_LocalFunctionsAndLambdasTests
{
    private const string ID = nameof(SYN_07_08_LocalFunctionsAndLambdasTests);

    public static readonly string Sources = $$"""
    using System;
    using Kassyi.GSharp.Core;

    class Dummy_{{ID}} {
        public void LocalFunctionHappyPath(uint[] data) {
            int Square(int x) => x * x;
            data[0] = (uint)Square(5);
        }

        public void LocalFunctionSadPath(uint[] data) {
            string text = "invalid";
            void BadLocal() {
                var len = text.Length;
            }
            BadLocal();
        }

        public void LocalFunctionScopeCollision(uint[] data, bool cond) {
            if (cond) {
                int Calculate(int a) => a + 1;
                data[0] = (uint)Calculate(10);
            } else {
                int Calculate(int a) => a * 2;
                data[0] = (uint)Calculate(10);
            }
        }

        public void LocalFunctionRecursive(uint[] data) {
            int Factorial(int n) {
                if (n <= 1) return 1;
                return n * Factorial(n - 1);
            }
            data[0] = (uint)Factorial(5);
        }

        public void LambdaHappyPath(uint[] data) {
            var addOne = (int x) => x + 1;
            int factor = 10;
            var mult = (int x) => x * factor;
            data[0] = (uint)addOne(5) + (uint)mult(3);
        }

        public void LambdaSadPathDelegate(uint[] data) {
            Func<int, int> f = x => x + 1;
            data[0] = (uint)f(5);
        }

        public void LambdaEdgeCaseImmediate(uint[] data) {
            var res = ((Func<int, int>)(x => x * 2))(10);
            data[0] = (uint)res;
        }

        public void LambdaEdgeCaseNested(uint[] data) {
            var outer = (int a) => {
                var inner = (int b) => a + b;
                return inner(5);
            };
            data[0] = (uint)outer(10);
        }
    }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource(Sources);

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "Syntax")]
    public void Test_GSH03_SYN07_LocalFunction_Transpilation_HappyPath()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LocalFunctionHappyPath");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("auto Square=[=] __device__ (int x) -> int", cudaCode);
        Assert.Contains("Square(5)", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH03_SYN07_LocalFunction_SadPath_ReferenceTypeCapture()
    {
        var code = $$"""
        using System;
        using Kassyi.GSharp.Core;

        class SadPathKernel {
            [GpuKernel]
            public void Kernel(uint[] data) {
                string text = "invalid";
                void BadLocal() {
                    var len = text.Length;
                }
                BadLocal();
            }
        }
        """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(code, new GpuKernelAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.HeapClosureNotSupported.Id || d.Id == GSharpDiagnostics.StringNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "Syntax")]
    public void Test_GSH03_SYN07_LocalFunction_EdgeCase_NameCollisionAcrossScopes()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LocalFunctionScopeCollision");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("auto Calculate=[=] __device__ (int a) -> int", cudaCode);
        Assert.Contains("Calculate(10)", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "Syntax")]
    public void Test_GSH03_SYN07_LocalFunction_EdgeCase_RecursiveLocalFunction()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LocalFunctionRecursive");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("struct __rec_Factorial", cudaCode);
        Assert.Contains("__rec_Factorial::call", cudaCode);
        Assert.Contains("Factorial(5)", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "Syntax")]
    public void Test_SYN08_LambdaExpressions_HappyPath_NoCaptureAndValueCapture()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LambdaHappyPath");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("auto addOne=[=] __device__ (int x)", cudaCode);
        Assert.Contains("auto mult=[=] __device__ (int x)", cudaCode);
        Assert.Contains("addOne(5)", cudaCode);
        Assert.Contains("mult(3)", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN08_LambdaExpressions_SadPath_DelegateEscaping()
    {
        var code = $$"""
        using System;
        using Kassyi.GSharp.Core;

        class DelegateKernel {
            [GpuKernel]
            public void Kernel(uint[] data) {
                Func<int, int> f = x => x + 1;
                data[0] = (uint)f(5);
            }
        }
        """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(code, new GpuKernelAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.DelegateNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "Syntax")]
    public void Test_SYN08_LambdaExpressions_EdgeCase_ImmediatelyEvaluatedLambda()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LambdaEdgeCaseImmediate");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("([=] __device__ (int x)", cudaCode);
        Assert.Contains("(10)", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "Syntax")]
    public void Test_SYN08_LambdaExpressions_EdgeCase_NestedLambdas()
    {
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "LambdaEdgeCaseNested");
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();

        var cudaCode = MethodTranspiler.GenerateCudaSource(syntax, model);

        Assert.Contains("auto outer=[=] __device__ (int a)", cudaCode);
        Assert.Contains("auto inner=[=] __device__ (int b)", cudaCode);
        Assert.Contains("inner(5)", cudaCode);
        Assert.Contains("outer(10)", cudaCode);
    }
}
