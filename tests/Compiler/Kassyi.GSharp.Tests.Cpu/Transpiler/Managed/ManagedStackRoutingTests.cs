#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ManagedStackRoutingTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedStackEligibilityCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetTargetPrefix_StackFunctionInAStackBody_IsTheStackFormPrefix()
    {
        var method = GetMethod("EligibilityKernels", "StackDeviceWait");

        Assert.Equal(GSharpInternalNames.StackModel.MethodPrefix,
            ManagedRouting.GetTargetPrefix(method, s_compilation, ManagedBodyKind.StackBody));
        Assert.Equal(GSharpInternalNames.StackModel.MethodPrefix,
            ManagedRouting.GetRoutedReferencePrefix(method, s_compilation, ManagedBodyKind.StackBody));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetTargetPrefix_StackFunctionInAnotherBody_StaysTheUsersMethod()
    {
        var method = GetMethod("EligibilityKernels", "StackDeviceWait");

        Assert.Null(ManagedRouting.GetTargetPrefix(method, s_compilation, ManagedBodyKind.Kernel));
        Assert.Null(ManagedRouting.GetTargetPrefix(method, s_compilation, ManagedBodyKind.SuspendingDeviceFunction));
        Assert.Null(ManagedRouting.GetTargetPrefix(method, s_compilation, ManagedBodyKind.ManagedVersion));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetTargetPrefix_SuspendingFunctionWithoutAStackForm_Throws()
    {
        var method = GetMethod("WarpEligibilityKernels", "RecursiveShuffle");

        Assert.Throws<InvalidOperationException>(() =>
            ManagedRouting.GetTargetPrefix(method, s_compilation, ManagedBodyKind.StackBody));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetRoutedExtensionCall_ReducedCallInAStackBody_ReturnsTheExtensionAndTheStackFormPrefix()
    {
        var symbol = GetBoundSymbol<InvocationExpressionSyntax>("CallsStackExtension",
            static node => node.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WaitAndAdd" });

        var routed = ManagedRouting.GetRoutedExtensionCall(symbol, s_compilation, ManagedBodyKind.StackBody);

        Assert.NotNull(routed);
        Assert.True(SymbolEqualityComparer.Default.Equals(GetMethod("EligibilityKernels", "WaitAndAdd"),
            routed.Value.Extension));
        Assert.Equal(GSharpInternalNames.StackModel.MethodPrefix, routed.Value.Prefix);
        Assert.Null(ManagedRouting.GetRoutedExtensionCall(symbol, s_compilation, ManagedBodyKind.Kernel));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBoundExtension_ReducedGroupInAStackBody_ReturnsTheExtensionAndTheStackFormPrefix()
    {
        var symbol = GetBoundSymbol<MemberAccessExpressionSyntax>("PassesStackExtensionGroup",
            static node => node.Name.Identifier.ValueText == "WaitAt");

        var bound = ManagedRouting.GetBoundExtension(symbol, s_compilation, ManagedBodyKind.StackBody);

        Assert.NotNull(symbol.ReducedFrom);
        Assert.NotNull(bound);
        Assert.True(SymbolEqualityComparer.Default.Equals(GetMethod("EligibilityKernels", "WaitAt"),
            bound.Value.Extension));
        Assert.Equal(GSharpInternalNames.StackModel.MethodPrefix, bound.Value.Prefix);
        Assert.Null(ManagedRouting.GetBoundExtension(symbol, s_compilation, ManagedBodyKind.Kernel));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AccessorHolder_StackFormAndManagedVersionOfOneFunction_HaveDifferentNames()
    {
        var caller = GetMethod("EligibilityKernels", "StackDeviceWait");

        var version = GetHolderName(new ManagedAccessorSet(caller, s_compilation).Spell(caller, "Target"));
        var stack = GetHolderName(new ManagedAccessorSet(caller, s_compilation, isStackForm: true)
            .Spell(caller, "Target"));

        Assert.StartsWith(GSharpInternalNames.ManagedVersion.AccessorPrefix, version, StringComparison.Ordinal);
        Assert.StartsWith(GSharpInternalNames.StackModel.AccessorPrefix, stack, StringComparison.Ordinal);
        Assert.False(stack.StartsWith(GSharpInternalNames.ManagedVersion.AccessorPrefix, StringComparison.Ordinal));
        Assert.False(version.StartsWith(GSharpInternalNames.StackModel.AccessorPrefix, StringComparison.Ordinal));
    }

    // global::<type>.<holder>.Invoke: the holder
    private static string GetHolderName(ExpressionSyntax spelled) =>
        spelled is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: var holder } }
            ? holder.Identifier.ValueText
            : throw new InvalidOperationException("The stub is not spelled as <type>.<holder>.Invoke.");

    private static IMethodSymbol GetBoundSymbol<TNode>(string kernelName, Func<TNode, bool> match)
        where TNode : SyntaxNode
    {
        var kernel = GetMethod("EligibilityKernels", kernelName);
        var declaration = kernel.DeclaringSyntaxReferences[0].GetSyntax();
        var model = s_compilation.GetSemanticModel(declaration.SyntaxTree);
        var node = declaration.DescendantNodes().OfType<TNode>().First(match);
        return model.GetSymbolInfo(node).Symbol as IMethodSymbol ??
            throw new InvalidOperationException($"The node in {kernelName} binds to no method.");
    }

    private static IMethodSymbol GetMethod(string typeName, string methodName)
    {
        var type = s_compilation.GetTypeByMetadataName($"ManagedStackEligibilityCases.{typeName}") ??
            throw new InvalidOperationException($"Type '{typeName}' not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }
}
