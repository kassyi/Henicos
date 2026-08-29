using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class GEN_17_RefOutDereferenceTests
{
    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(string code, string methodName)
    {
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
    [Trait("FeatureId", "GEN-17")]
    [Trait("Category", "Syntax")]
    public void Test_GEN17_RefOutDereferencing_HappyPath_ModifyingRefArgument()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void ModifyRef(ref bool successFlag)
            {
                successFlag = false;
            }
        }
        """;

        var (cudaSource, diagnostics) = TranspileMethod(code, "ModifyRef");

        Assert.Empty(diagnostics);
        Assert.Contains("(*successFlag)", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "GEN-17")]
    [Trait("Category", "Syntax")]
    public void Test_GEN17_RefOutDereferencing_EdgeCase_MemberAccessAndChainedAssignments()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public struct DummyStruct
        {
            public int Field;
        }

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void MemberAccessRef(ref DummyStruct refArg)
            {
                refArg.Field = 5;
            }

            [GpuDeviceFunction]
            public static void ChainedRefAssignment(ref int refArg, out int a, out int b)
            {
                a = b = refArg = 10;
            }
        }
        """;

        var (cudaSourceMember, diagnosticsMember) = TranspileMethod(code, "MemberAccessRef");
        Assert.Empty(diagnosticsMember);
        Assert.Contains("(*refArg).Field", cudaSourceMember);

        var (cudaSourceChained, diagnosticsChained) = TranspileMethod(code, "ChainedRefAssignment");
        Assert.Empty(diagnosticsChained);
        Assert.Contains("(*refArg)", cudaSourceChained);
        Assert.Contains("(*a)", cudaSourceChained);
        Assert.Contains("(*b)", cudaSourceChained);
    }
}
