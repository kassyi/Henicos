#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StepNarrowingAnalysisTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/StepNarrowingCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("SByteStep", SpecialType.System_SByte)]
    [InlineData("ByteStep", SpecialType.System_Byte)]
    [InlineData("ShortStep", SpecialType.System_Int16)]
    [InlineData("UShortStep", SpecialType.System_UInt16)]
    [InlineData("CharStep", SpecialType.System_Char)]
    [InlineData("ByteEnumStep", SpecialType.System_Byte)]
    [InlineData("SByteEnumStep", SpecialType.System_SByte)]
    [InlineData("ShortEnumStep", SpecialType.System_Int16)]
    [InlineData("UShortEnumStep", SpecialType.System_UInt16)]
    public void Test_GetStepNarrowing_NarrowTypes_ReturnsTargetType(string method, SpecialType expected)
    {
        var op = GetStepOperation(method);
        var narrowing = IntegerSemantics.GetStepNarrowing(op);
        Assert.NotNull(narrowing);
        Assert.Equal(expected, narrowing.SpecialType);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("IntStep")]
    [InlineData("UIntStep")]
    [InlineData("LongStep")]
    [InlineData("ULongStep")]
    [InlineData("FloatStep")]
    [InlineData("DoubleStep")]
    [InlineData("DecimalStep")]
    [InlineData("NIntStep")]
    [InlineData("NUIntStep")]
    [InlineData("IntEnumStep")]
    public void Test_GetStepNarrowing_NonNarrowTypes_ReturnsNull(string method)
    {
        var op = GetStepOperation(method);
        var narrowing = IntegerSemantics.GetStepNarrowing(op);
        Assert.Null(narrowing);
    }

    private static IIncrementOrDecrementOperation GetStepOperation(string method)
    {
        var tree = s_compilation.SyntaxTrees.First();
        var model = s_compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.ValueText == method);
        var exprStatement = declaration.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .First();
        var op = model.GetOperation(exprStatement.Expression) as IIncrementOrDecrementOperation;
        return op ?? throw new InvalidOperationException($"'{method}' has no increment/decrement operation.");
    }
}
