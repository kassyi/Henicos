#nullable enable
using System;
using System.Linq;
using System.Threading;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrTraps"/> and <see cref="IrEffects"/> trap determinations (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrTrapsTests
{
    private const string TestCase = "Ir/IrTrapCases.cs";
    private const string TypeName = "IrTrapCases.Traps";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_DivisionByAnyInt_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("DivideByAny");
        var result = IrTestCases.Result(irMethod);

        Assert.True(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_DivisionByPositiveDivisor_ReturnsFalse()
    {
        var (irMethod, _, _, _) = GetMethod("DivideByPositive");
        var result = IrTestCases.Result(irMethod);

        Assert.False(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_CheckedAddition_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("CheckedAdd");
        var result = IrTestCases.Result(irMethod);

        Assert.True(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_ElementRead_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("ElementRead");
        var result = IrTestCases.Result(irMethod);

        Assert.True(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_LengthOfAnArray_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("LengthRead");
        var result = IrTestCases.Result(irMethod);

        Assert.True(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_NullableUnwrap_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("UnwrapNullable");
        var result = IrTestCases.Result(irMethod);

        Assert.True(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_FloatingPointMath_ReturnsFalse()
    {
        var (irMethod, _, _, _) = GetMethod("FloatMath");
        var result = IrTestCases.Result(irMethod);

        Assert.False(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_IsNullTest_ReturnsFalse()
    {
        var (irMethod, _, _, _) = GetMethod("IsNullTest");
        var result = IrTestCases.Result(irMethod);

        Assert.False(IrTraps.MayTrap(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrapBeforeStore_ElementStoreTarget_ReturnsFalse()
    {
        var (irMethod, _, _, _) = GetMethod("ElementStore");
        var target = GetAssignmentTarget(irMethod);

        Assert.False(IrTraps.MayTrapBeforeStore(target));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MayTrapBeforeStore_ULongIndex_ReturnsTrue()
    {
        var (irMethod, _, _, _) = GetMethod("ElementStoreAtULong");
        var target = GetAssignmentTarget(irMethod);

        Assert.True(IrTraps.MayTrapBeforeStore(target));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OfAssignmentTarget_ElementStore_DoesNotThrowAndWritesNothing()
    {
        var (irMethod, _, _, compilation) = GetMethod("ElementStore");
        var target = GetAssignmentTarget(irMethod);

        var effects = IrEffects.OfAssignmentTarget(target, compilation);

        Assert.False(effects.MayThrow);
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OfWrittenLocation_ElementStore_MayThrow()
    {
        var (irMethod, _, _, compilation) = GetMethod("ElementStore");
        var target = GetAssignmentTarget(irMethod);

        var effects = IrEffects.OfWrittenLocation(target, compilation);

        Assert.True(effects.MayThrow);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_EveryCase_AgreesWithExpressionEffects()
    {
        string[] cases =
        [
            "DivideByAny",
            "DivideByPositive",
            "CheckedAdd",
            "ElementRead",
            "LengthRead",
            "UnwrapNullable",
            "FloatMath"
        ];

        foreach (var caseName in cases)
        {
            var (irMethod, syntax, model, compilation) = GetMethod(caseName);
            var result = IrTestCases.Result(irMethod);
            var initSyntax = GetResultInitializerSyntax(syntax);

            var irEffects = IrEffects.Of(result, compilation);
            var syntaxEffects = ExpressionEffects.Of(initSyntax, model);

            Assert.Equal(syntaxEffects.MayThrow, irEffects.MayThrow);
        }
    }

    private static (IrMethod Method, MethodDeclarationSyntax Syntax, SemanticModel Model, Compilation Compilation)
        GetMethod(string name)
    {
        var (compilation, _, model) = RoslynTestUtility.GetTestCaseContext(TestCase);
        var methodSymbol = IrTestCases.FindMethod(compilation, TypeName, name);
        var tree = methodSymbol.Locations[0].SourceTree!;
        var syntax = tree.GetRoot().FindNode(methodSymbol.Locations[0].SourceSpan)
            .FirstAncestorOrSelf<MethodDeclarationSyntax>()!;
        var irMethod = IrBuilder.TryBuild(methodSymbol, compilation, CancellationToken.None)!;
        irMethod = IrClassifier.Classify(irMethod, compilation);
        return (irMethod, syntax, model, compilation);
    }

    private static ExpressionSyntax GetResultInitializerSyntax(MethodDeclarationSyntax methodSyntax)
    {
        var localDecl = methodSyntax.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
        var declarator = localDecl.Declaration.Variables.Single(v => v.Identifier.Text == "result");
        return declarator.Initializer!.Value;
    }

    private static IrExpression GetAssignmentTarget(IrMethod method)
    {
        var statement = (IrExpressionStatement)IrTestCases.Statement(method, 0);
        var assignment = (IrAssignment)statement.Expression;
        return assignment.Target;
    }
}
