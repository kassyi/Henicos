#nullable enable
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;
using Kassyi.GSharp.Tests.Shared;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
public class ManagedRewriteFormsTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedVersionCases.cs")
    );

    private static readonly Compilation s_formsCompilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Managed/ManagedFormCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsSequentialSum_FloatSumOnStackEnumerable_IsTrue()
    {
        var method = GetInvocationSymbol("Functions", "FloatSumDirect", "Sum");
        Assert.True(ManagedRewriteClassifier.IsSequentialSum(method));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsSequentialSum_DoubleSumWithSelector_IsTrue()
    {
        var method = GetInvocationSymbol("Functions", "DoubleSumSelector", "Sum");
        Assert.True(ManagedRewriteClassifier.IsSequentialSum(method));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsSequentialSum_IntSum_IsFalse()
    {
        var method = GetInvocationSymbol("Functions", "IntSumPlain", "Sum");
        Assert.False(ManagedRewriteClassifier.IsSequentialSum(method));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsInlinePtx_GpuInlinePtx_IsTrue()
    {
        var method = GetInvocationSymbol("Functions", "InlinePtxDirect", "InlinePtx");
        Assert.True(DialectCallSupport.IsInlinePtx(method));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsInlinePtx_UserMethodNamedInlinePtx_IsFalse()
    {
        var method = GetInvocationSymbol("Functions", "UserInlinePtx", "InlinePtx");
        Assert.False(DialectCallSupport.IsInlinePtx(method));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsInlinePtxStatement_NotAStatement_IsFalse()
    {
        var method = GetInvocationSymbol("Functions", "InlinePtxDirect", "InlinePtx");
        Assert.False(ManagedRewriteClassifier.IsInlinePtxStatement(method, isExpressionStatement: false));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsUnsupportedInlinePtx_CpuDialect_IsTrue()
    {
        var method = GetInvocationSymbol("Functions", "InlinePtxDirect", "InlinePtx");
        Assert.True(DialectCallSupport.IsUnsupportedInlinePtx(method, HostDialects.Cpu));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsCallerInfoParameter_OptionalCallerLine_IsTrue()
    {
        var forms = s_formsCompilation.GetTypeByMetadataName("ManagedFormCases.Forms") ??
            throw new InvalidOperationException("ManagedFormCases.Forms not found.");
        var lineMethod = forms.GetMembers("Line").OfType<IMethodSymbol>().First();
        var parameter = lineMethod.Parameters[0];

        Assert.True(CallerInfoArgument.IsCallerInfoParameter(parameter));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IsCallerInfoParameter_PlainOptional_IsFalse()
    {
        var forms = s_formsCompilation.GetTypeByMetadataName("ManagedFormCases.Forms") ??
            throw new InvalidOperationException("ManagedFormCases.Forms not found.");
        var plainMethod = forms.GetMembers("Plain").OfType<IMethodSymbol>().First();
        var parameter = plainMethod.Parameters[0];

        Assert.False(CallerInfoArgument.IsCallerInfoParameter(parameter));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsCallerInfoParameter_CallerLineWithoutDefault_IsFalse()
    {
        const string source = """
            #pragma warning disable CS4020
            namespace Test;
            public static class C
            {
                public static void M([System.Runtime.CompilerServices.CallerLineNumber] int line) {}
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var method = (compilation.GetTypeByMetadataName("Test.C") ??
            throw new InvalidOperationException("Test.C not found."))
            .GetMembers("M").OfType<IMethodSymbol>().First();
        var parameter = method.Parameters[0];

        Assert.False(CallerInfoArgument.IsCallerInfoParameter(parameter));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AddsCallerInfo_MethodOfTheCompilation_IsTrue()
    {
        var forms = s_formsCompilation.GetTypeByMetadataName("ManagedFormCases.Forms") ??
            throw new InvalidOperationException("ManagedFormCases.Forms not found.");
        var lineMethod = forms.GetMembers("Line").OfType<IMethodSymbol>().First();

        Assert.True(ManagedCallForms.AddsCallerInfo(lineMethod));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void AddsCallerInfo_MethodOfMetadata_IsFalse()
    {
        var math = s_compilation.GetTypeByMetadataName("System.Math") ??
            throw new InvalidOperationException("System.Math not found.");
        var maxMethod = math.GetMembers("Max").OfType<IMethodSymbol>()
            .First(m => m.Parameters.Length == 2 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32);

        Assert.False(ManagedCallForms.AddsCallerInfo(maxMethod));
    }

    private static IMethodSymbol GetInvocationSymbol(string type, string callerMethodName, string calleeName)
    {
        var caller = (s_compilation.GetTypeByMetadataName($"ManagedVersionCases.{type}") ??
                      throw new InvalidOperationException($"Type '{type}' not found."))
            .GetMembers(callerMethodName)
            .OfType<IMethodSymbol>()
            .First();
        var syntax = caller.DeclaringSyntaxReferences[0].GetSyntax();
        var model = s_compilation.GetSemanticModel(syntax.SyntaxTree);
        var invocation = syntax.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText == calleeName,
                SimpleNameSyntax s => s.Identifier.ValueText == calleeName,
                _ => false
            });
        return (IMethodSymbol?)model.GetSymbol(invocation) ??
            throw new InvalidOperationException("The call binds to no method.");
    }
}
