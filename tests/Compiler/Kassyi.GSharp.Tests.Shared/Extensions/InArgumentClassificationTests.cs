#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Extensions;

/// <summary>
/// Classifies the argument of an in / ref parameter: whether C# passes a variable
/// (<see cref="OperationExtensions.IsVariable"/>), and how the CPU lowering passes it to a call that may suspend
/// (<see cref="SpilledArguments.ClassifyByReference"/>), which GSH0062 reports when the variable would be selected
/// again on every resume (#1585).
/// </summary>
[Category(TestCategory.Compiler)]
public class InArgumentClassificationTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Extensions/InArgumentCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Local")]
    [InlineData("ParenthesizedLocal")]
    [InlineData("ArrayElement")]
    [InlineData("ArrayOfACall")]
    [InlineData("InlineElement")]
    [InlineData("ParenthesizedInlineElement")]
    [InlineData("FixedElement")]
    [InlineData("PointerIndirection")]
    [InlineData("PointerElement")]
    [InlineData("PointerMember")]
    [InlineData("DereferencedMember")]
    [InlineData("KeywordPointerIndirection")]
    public void ClassifyByReference_VariableWithAKeptRoot_IsALocation(string name)
    {
        var (argument, model) = GetArgument(name);

        Assert.True(IsVariable(argument, model));
        Assert.Equal(ByReferencePassing.Location, SpilledArguments.ClassifyByReference(argument, model));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("RefConditional")]
    [InlineData("ParenthesizedRefConditional")]
    [InlineData("RefReturn")]
    [InlineData("ElementOfARefReturn")]
    [InlineData("RefProperty")]
    public void ClassifyByReference_VariableThatACallSelects_IsReselected(string name)
    {
        var (argument, model) = GetArgument(name);

        Assert.True(IsVariable(argument, model));
        Assert.Equal(ByReferencePassing.ReselectedLocation, SpilledArguments.ClassifyByReference(argument, model));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ElementOfAValue", false)]
    [InlineData("Sum", false)]
    [InlineData("SpanElement", true)]
    public void ClassifyByReference_ValueOrSpanElement_IsAValue(string name, bool isVariable)
    {
        var (argument, model) = GetArgument(name);

        // A span element is a variable that the CPU paths still copy (#990)
        Assert.Equal(isVariable, IsVariable(argument, model));
        Assert.Equal(ByReferencePassing.Value, SpilledArguments.ClassifyByReference(argument, model));
    }

    private static bool IsVariable(ArgumentSyntax argument, SemanticModel model)
    {
        var expression = argument.Expression;
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return model.GetOperation(expression).IsVariable();
    }

    private static (ArgumentSyntax Argument, SemanticModel Model) GetArgument(string name)
    {
        var type = s_compilation.GetTypeByMetadataName("InArgumentCases.Shapes") ??
                   throw new InvalidOperationException("Type 'InArgumentCases.Shapes' not found.");
        var method = type.GetMembers(name).OfType<IMethodSymbol>().First();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var call = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        return (call.ArgumentList.Arguments[0], s_compilation.GetSemanticModel(declaration.SyntaxTree));
    }
}
