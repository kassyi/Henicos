#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// The closed copy of a call that is written as its argument (<c>T.Abs(x)</c> of an unsigned type) around an argument
/// that is itself the root of a rewrite: both rewrites keep the number of their own node.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopyNestedCallTests
{
    // Calls of T.Abs around another rewrite: a call, an operator, a negation, a constant, a math call
    public static TheoryData<string, string> NestedCallCases { get; } = new()
    {
        { "AbsOfAbs", "System.Byte" },
        { "AbsOfAbs", "System.Char" },
        { "AbsOfAbs", "System.UInt32" },
        { "AbsOfAbs", "System.UInt64" },
        { "AbsOfAbs", "System.UIntPtr" },
        { "AbsOfAbs", "System.Int32" },
        { "AbsOfSum", "System.Byte" },
        { "AbsOfNegation", "System.UInt32" },
        { "AbsOfMaxValue", "System.UInt32" },
        { "AbsOfAbsPlusB", "System.Byte" },
        { "AbsOfMax", "System.Char" },
    };

    // The forms whose values are compared, with the types that narrow or wrap
    public static TheoryData<string, string> NestedCallValueCases { get; } = new()
    {
        { "AbsOfAbs", "System.Byte" },
        { "AbsOfSum", "System.Byte" },
        { "AbsOfAbsPlusB", "System.Byte" },
        { "AbsOfMax", "System.Char" },
        { "AbsOfAbs", "System.UInt64" },
        { "AbsOfNegation", "System.UInt32" },
    };

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(NestedCallCases))]
    public void Create_NestedArgumentCall_GivesACopyThatCompiles(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);

        Assert.True(copy.IsFaithful, Describe(copy));
        var errors = ModelOf(set, copy.Declaration!)
            .GetDiagnostics(copy.Declaration!.Span)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id);
        Assert.Empty(errors);
    }

    // The outer call is a new parenthesized node around the inner one, which keeps its own number
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("System.Byte")]
    [InlineData("System.UInt32")]
    [InlineData("System.UIntPtr")]
    public void Create_NestedArgumentCall_KeepsTheNodeOfTheInnerCall(string typeArgument)
    {
        var (copy, set) = CreateCopy("AbsOfAbs", typeArgument);
        var type = set.Compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException(typeArgument);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(
            Instance(set.Compilation, "AbsOfAbs", type).Definition.DeclaringSyntaxReferences.Single().GetSyntax());
        var nodes = CopyNodeIndex.List(declaration);
        var calls = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.Equal(2, calls.Length);
        var outer = Array.IndexOf(nodes, calls[0]);
        var inner = Array.IndexOf(nodes, calls[1]);

        var innerNode = Assert.Single(
            Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration).DescendantNodes(),
            node => CopyNodeIndex.TryGet(node, out var index) && index == inner);
        Assert.IsType<ParenthesizedExpressionSyntax>(innerNode);
        var parent = Assert.IsType<ParenthesizedExpressionSyntax>(innerNode.Parent);
        Assert.True(CopyNodeIndex.TryGet(parent, out var parentIndex) && parentIndex == outer);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(NestedCallValueCases))]
    public void Emit_NestedArgumentCall_ComputesWhatTheDeclarationComputes(string method, string typeArgument) =>
        GenericCopyValueSupport.AssertCopyComputesWhatTheDeclarationComputes(method, typeArgument);
}
