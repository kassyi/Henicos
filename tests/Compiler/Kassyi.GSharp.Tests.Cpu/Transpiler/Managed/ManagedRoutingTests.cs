#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
public class ManagedRoutingTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedVersionCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsRoutedReference_DeviceFunctionWithAManagedVersion_IsTrue()
    {
        var method = GetMethod("Functions", "Direct");
        Assert.True(ManagedRouting.IsRoutedReference(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsRoutedReference_DeviceFunctionWithoutOne_IsFalse()
    {
        var method = GetMethod("Functions", "Plain");
        Assert.False(ManagedRouting.IsRoutedReference(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsRoutedReference_ReducedExtension_IsFalse()
    {
        var method = GetMethod("Functions", "CallsExtension");
        var model = s_compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);
        var invocation = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<InvocationExpressionSyntax>().First();
        var symbol = (IMethodSymbol?)model.GetSymbol(invocation) ??
            throw new InvalidOperationException("The call binds to no method.");

        Assert.NotNull(symbol.ReducedFrom);
        Assert.False(ManagedRouting.IsRoutedReference(symbol, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBoundExtension_ReducedExtensionGroupWithAVersion_ReturnsTheDeclaredExtension()
    {
        var method = GetMethod("Functions", "PassesExtensionMethodGroup");
        var model = s_compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);
        var memberAccess = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.ValueText == "AddToIndex");
        var symbol = (IMethodSymbol?)model.GetSymbol(memberAccess) ??
            throw new InvalidOperationException("The member access binds to no method.");

        var expected = GetMethod("Extensions", "AddToIndex");
        var bound = ManagedRouting.GetBoundExtension(symbol, s_compilation);

        Assert.NotNull(bound);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected, bound));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetBoundExtension_NotReduced_ReturnsNull()
    {
        var method = GetMethod("Functions", "Direct");
        Assert.Null(ManagedRouting.GetBoundExtension(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetBoundExtension_ExtensionWithoutTheAttribute_ReturnsNull()
    {
        const string source = """
            namespace Test;
            public static class Extensions
            {
                public static void NonDeviceExt(this int[] x, int i) {}
            }
            public static class Caller
            {
                public static void Call(int[] x)
                {
                    System.Action<int> a = x.NonDeviceExt;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var caller = (compilation.GetTypeByMetadataName("Test.Caller") ??
            throw new InvalidOperationException("Test.Caller not found."))
            .GetMembers("Call").OfType<IMethodSymbol>().First();
        var model = compilation.GetSemanticModel(caller.DeclaringSyntaxReferences[0].SyntaxTree);
        var memberAccess = caller.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.ValueText == "NonDeviceExt");
        var symbol = (IMethodSymbol?)model.GetSymbol(memberAccess) ??
            throw new InvalidOperationException("The member access binds to no method.");

        Assert.NotNull(symbol.ReducedFrom);
        Assert.Null(ManagedRouting.GetBoundExtension(symbol, compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetRoutedExtensionCall_ReducedCallOfAFunctionWithAVersion_ReturnsTheExtension()
    {
        var method = GetMethod("Functions", "CallsExtension");
        var model = s_compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);
        var invocation = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<InvocationExpressionSyntax>().First();
        var symbol = (IMethodSymbol?)model.GetSymbol(invocation) ??
            throw new InvalidOperationException("The call binds to no method.");

        var expected = GetMethod("Extensions", "Doubled");
        var routed = ManagedRouting.GetRoutedExtensionCall(symbol, s_compilation);

        Assert.NotNull(routed);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected, routed));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetMemberName_Routed_IsTheManagedVersionName()
    {
        var method = GetMethod("Functions", "Direct");
        var expectedRoutedName = GlobalCppNames.GetKernelDerivedName(
            GSharpInternalNames.ManagedVersion.MethodPrefix, method);

        Assert.Equal(expectedRoutedName, ManagedRouting.GetMemberName(method, routed: true));
        Assert.Equal("Direct", ManagedRouting.GetMemberName(method, routed: false));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetAccessorName_NotRouted_IsTheNameAsDeclared()
    {
        var method = GetMethod("Functions", "Plain");
        Assert.Equal("Plain", ManagedRouting.GetAccessorName(method, routed: false));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBindTypeArguments_ReducedGroup_ListsReceiverParametersAndNoVoidReturn()
    {
        var method = GetMethod("Functions", "PassesExtensionMethodGroup");
        var model = s_compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);
        var memberAccess = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.ValueText == "AddToIndex");
        var symbol = (IMethodSymbol?)model.GetSymbol(memberAccess) ??
            throw new InvalidOperationException("The member access binds to no method.");
        var extension = symbol.ReducedFrom ??
            throw new InvalidOperationException("The method is not a reduced extension method.");

        var typeArguments = ManagedRouting.GetBindTypeArguments(symbol, extension);

        Assert.Equal(2, typeArguments.Length);
        Assert.True(typeArguments[0] is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 });
        Assert.Equal(SpecialType.System_Int32, typeArguments[1].SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBindTypeArguments_GenericReducedGroup_UsesTheInferredReceiver()
    {
        var method = GetMethod("Functions", "PassesGenericExtensionMethodGroup");
        var model = s_compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree);
        var memberAccess = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.ValueText == "AddGeneric");
        var symbol = (IMethodSymbol?)model.GetSymbol(memberAccess) ??
            throw new InvalidOperationException("The member access binds to no method.");
        var extension = symbol.ReducedFrom ??
            throw new InvalidOperationException("The method is not a reduced extension method.");

        var typeArguments = ManagedRouting.GetBindTypeArguments(symbol, extension);

        Assert.Equal(2, typeArguments.Length);
        Assert.True(typeArguments[0] is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 });
        Assert.Equal(SpecialType.System_Int32, typeArguments[1].SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SpellCallee_ReceiverImplicitThisAndStatic_SpellsExpectedSyntax()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace SpellCalleeCases;

            public partial struct S
            {
                [GpuDeviceFunction]
                public int Inst() => 1;

                [GpuDeviceFunction]
                public int InstGen<T>() => 2;

                [GpuDeviceFunction]
                public static int Stat() => 3;
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var type = compilation.GetTypeByMetadataName("SpellCalleeCases.S")
            ?? throw new InvalidOperationException("SpellCalleeCases.S not found.");
        var inst = type.GetMembers("Inst").OfType<IMethodSymbol>().Single();
        var instGen = type.GetMembers("InstGen").OfType<IMethodSymbol>().Single();
        var stat = type.GetMembers("Stat").OfType<IMethodSymbol>().Single();

        const string prefix = GSharpInternalNames.ManagedVersion.MethodPrefix;
        var expectedInstName = GlobalCppNames.GetKernelDerivedName(prefix, inst);
        var expectedInstGenName = GlobalCppNames.GetKernelDerivedName(prefix, instGen);
        var expectedStatName = GlobalCppNames.GetKernelDerivedName(prefix, stat);

        var receiverExpr = SyntaxFactory.ParseExpression("s");
        var explicitReceiver = ManagedExecutionRewriter.SpellCallee(inst, null, prefix, receiverExpr);
        SyntaxAssert.Equivalent($"s.{expectedInstName}", explicitReceiver);

        var typeArgs = SyntaxFactory.TypeArgumentList(
            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))));
        var pointerReceiver = ManagedExecutionRewriter.SpellCallee(
            instGen,
            typeArgs,
            prefix,
            SyntaxFactory.ParseExpression("p"),
            SyntaxKind.PointerMemberAccessExpression);
        SyntaxAssert.Equivalent($"p->{expectedInstGenName}<int>", pointerReceiver);

        var implicitThis = ManagedExecutionRewriter.SpellCallee(inst, null, prefix, receiver: null);
        SyntaxAssert.Equivalent(expectedInstName, implicitThis);

        var staticCallee = ManagedExecutionRewriter.SpellCallee(stat, null, prefix, receiver: receiverExpr);
        SyntaxAssert.Equivalent($"global::SpellCalleeCases.S.{expectedStatName}", staticCallee);
    }

    private static IMethodSymbol GetMethod(string type, string name) =>
        (s_compilation.GetTypeByMetadataName($"ManagedVersionCases.{type}") ??
         throw new InvalidOperationException($"Type '{type}' not found."))
        .GetMembers(name)
        .OfType<IMethodSymbol>()
        .First();
}
