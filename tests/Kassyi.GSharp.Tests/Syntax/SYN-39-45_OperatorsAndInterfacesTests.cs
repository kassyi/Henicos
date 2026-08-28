using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_39_45_OperatorsAndInterfacesTests
{
    [Fact(Skip = "未実装: SYN-39 (明示的 / 暗黙的型変換演算子 implicit/explicit)")]
    [Trait("FeatureId", "SYN-39")]
    [Trait("Category", "Syntax")]
    public void Test_SYN39_ConversionOperators_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-40 (自動実装プロパティ { get; set; })")]
    [Trait("FeatureId", "SYN-40")]
    [Trait("Category", "Syntax")]
    public void Test_SYN40_AutoProperties_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-41 (計算式プロパティ =>)")]
    [Trait("FeatureId", "SYN-41")]
    [Trait("Category", "Syntax")]
    public void Test_SYN41_ExpressionBodiedProperties_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-42 (静的抽象メンバ static abstract in interfaces)")]
    [Trait("FeatureId", "SYN-42")]
    [Trait("Category", "Syntax")]
    public void Test_SYN42_StaticAbstractMembers_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-43 (checked / unchecked ブロック・式)")]
    [Trait("FeatureId", "SYN-43")]
    [Trait("Category", "Syntax")]
    public void Test_SYN43_CheckedUnchecked_Transpilation()
    {
    }

    [Fact]
    [Trait("FeatureId", "SYN-44")]
    [Trait("Category", "Syntax")]
    public void Test_SYN44_ExtensionMethods_HappyPath_StaticInvocationAndRefModifiers()
    {
        var source = """
            using Kassyi.GSharp.Core;

            public struct DummyStruct_SYN44
            {
                public int Value;
            }

            public static class DummyExtensions_SYN44
            {
                [GpuDeviceFunction]
                public static int AddVal(this ref DummyStruct_SYN44 s, int amount)
                {
                    return s.Value + amount;
                }

                [GpuDeviceFunction]
                public static int GetVal(this DummyStruct_SYN44 s)
                {
                    return s.Value;
                }
            }

            public class KernelClass_SYN44
            {
                [GpuKernel]
                public static void TestKernel(ref DummyStruct_SYN44 paramStruct)
                {
                    DummyStruct_SYN44 localStruct = default;
                    int res1 = localStruct.AddVal(10);
                    int res2 = paramStruct.AddVal(20);
                    int res3 = localStruct.GetVal();
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TestKernel");

        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, model);

        Assert.Contains("AddVal(&localStruct,10)", cudaSource);
        Assert.Contains("AddVal(paramStruct,20)", cudaSource);
        Assert.Contains("GetVal(localStruct)", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-44")]
    [Trait("Category", "Syntax")]
    public void Test_SYN44_ExtensionMethods_SadPath_MissingGpuDeviceFunctionAttribute()
    {
        var source = """
            using Kassyi.GSharp.Core;

            public struct DummyStruct_SYN44_Sad
            {
                public int Value;
            }

            public static class NonGpuExtensions_SYN44
            {
                // Missing [GpuDeviceFunction] attribute
                public static int UnsupportedExt(this DummyStruct_SYN44_Sad s)
                {
                    return s.Value;
                }
            }

            public class KernelClass_SYN44_Sad
            {
                [GpuKernel]
                public static void TestKernelSad(ref DummyStruct_SYN44_Sad paramStruct)
                {
                    int res = paramStruct.UnsupportedExt();
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TestKernelSad");

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, model, diagnostics.Add);

        var diag = Assert.Single(diagnostics);
        Assert.Equal("GSH0003", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("UnsupportedExt", diag.GetMessage());
    }

    [Fact]
    [Trait("FeatureId", "SYN-44")]
    [Trait("Category", "Syntax")]
    public void Test_SYN44_ExtensionMethods_EdgeCase_GenericTypeInferenceAndChaining()
    {
        var source = """
            using Kassyi.GSharp.Core;

            public struct ChainNode_SYN44
            {
                public int Value;
            }

            public static class GenericChainExtensions_SYN44
            {
                [GpuDeviceFunction]
                public static ChainNode_SYN44 Increment(this ChainNode_SYN44 node)
                {
                    node.Value += 1;
                    return node;
                }

                [GpuDeviceFunction]
                public static ChainNode_SYN44 DoubleValue(this ChainNode_SYN44 node)
                {
                    node.Value *= 2;
                    return node;
                }

                [GpuDeviceFunction]
                public static T Identity<T>(this T item, T defaultVal)
                {
                    return item;
                }
            }

            public class KernelClass_SYN44_Edge
            {
                [GpuKernel]
                public static void TestKernelEdge(ref ChainNode_SYN44 node)
                {
                    ChainNode_SYN44 chainedResult = node.Increment().DoubleValue();
                    int num = 42;
                    int genResult = num.Identity(100);
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TestKernelEdge");

        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, model);

        Assert.Contains("DoubleValue(Increment(node))", cudaSource);
        Assert.Contains("Identity<int>(num,100)", cudaSource);
    }

    [Fact(Skip = "未実装: SYN-45 (Nullable 値型 uint?, Nullable<T>)")]
    [Trait("FeatureId", "SYN-45")]
    [Trait("Category", "Syntax")]
    public void Test_SYN45_NullableValueTypes_Transpilation()
    {
    }
}
