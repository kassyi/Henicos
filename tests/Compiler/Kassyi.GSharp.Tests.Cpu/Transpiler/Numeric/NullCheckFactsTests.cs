#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.ErrorSites;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Numeric;

/// <summary>
/// Unit tests for pure decision functions of <see cref="NullRepresentation"/>, <see cref="ArrayNullCheck"/>,
/// <see cref="ErrorSiteTable"/>, and <see cref="SourcePlace"/> (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class NullCheckFactsTests
{
    private static readonly Compilation Compilation = RoslynTestUtility.CreateCompilation("");
    private static readonly ITypeSymbol IntType = Compilation.GetSpecialType(SpecialType.System_Int32);
    private static readonly ITypeSymbol LongType = Compilation.GetSpecialType(SpecialType.System_Int64);
    private static readonly ITypeSymbol NullableIntType =
        Compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(IntType);
    private static readonly IArrayTypeSymbol IntArrayType = Compilation.CreateArrayTypeSymbol(IntType);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetNullForm_ArrayNullableAndInt_ReturnsTheirForms()
    {
        Assert.Equal(NullForm.ArrayView, NullRepresentation.GetNullForm(IntArrayType));
        Assert.Equal(NullForm.NullableValue, NullRepresentation.GetNullForm(NullableIntType));
        Assert.Equal(NullForm.None, NullRepresentation.GetNullForm(IntType));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CoalesceCastsToResult_NullableIntWithLongResult_ReturnsTrue()
    {
        Assert.True(NullRepresentation.CoalesceCastsToResult(NullableIntType, IntType, LongType));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CoalesceCastsToResult_SameTypes_ReturnsFalse()
    {
        Assert.False(NullRepresentation.CoalesceCastsToResult(NullableIntType, IntType, IntType));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CoalesceAssignmentReadsUnderlying_NullableTargetIntValue_ReturnsTrue()
    {
        Assert.True(NullRepresentation.CoalesceAssignmentReadsUnderlying(NullableIntType, IntType));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CoalesceAssignmentReadsUnderlying_ArrayTarget_ReturnsFalse()
    {
        Assert.False(NullRepresentation.CoalesceAssignmentReadsUnderlying(IntArrayType, IntArrayType));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetArgumentCheckKind_NonNullableArrayParameter_ReturnsArray()
    {
        var (trapCompilation, _, _) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var method = IrTestCases.FindMethod(trapCompilation, "IrTrapCases.Traps", "TakesArray");
        var parameter = method.Parameters[0];

        Assert.Equal(ArgumentCheckKind.Array, ArgumentNullCheck.GetArgumentCheckKind(parameter));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetArgumentCheckKind_NullableArrayParameter_ReturnsNone()
    {
        var (trapCompilation, _, _) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var method = IrTestCases.FindMethod(trapCompilation, "IrTrapCases.Traps", "TakesNullableArray");
        var parameter = method.Parameters[0];

        Assert.Equal(ArgumentCheckKind.None, ArgumentNullCheck.GetArgumentCheckKind(parameter));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ChecksArgument_ProvenArgument_ReturnsFalse()
    {
        var (trapCompilation, _, model) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var takesArrayMethod = IrTestCases.FindMethod(trapCompilation, "IrTrapCases.Traps", "TakesArray");
        var parameter = takesArrayMethod.Parameters[0];

        var passesNewMethod = IrTestCases.FindMethod(trapCompilation, "IrTrapCases.Traps", "PassesNew");
        var tree = passesNewMethod.Locations[0].SourceTree!;
        var syntax = tree.GetRoot().FindNode(passesNewMethod.Locations[0].SourceSpan)
            .FirstAncestorOrSelf<MethodDeclarationSyntax>()!;
        var invocation = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var argument = invocation.ArgumentList.Arguments[0].Expression;

        Assert.False(ArgumentNullCheck.ChecksArgument(argument, parameter, model));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateSite_LocationOfANode_MatchesTheNodeOverload()
    {
        var (trapCompilation, tree, model) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var root = tree.GetRoot();

        var binaryExpressions = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Take(3).ToList();
        Assert.Equal(3, binaryExpressions.Count);

        foreach (var node in binaryExpressions)
        {
            var fromNode = ErrorSiteTable.CreateSite(node, model, "TestMember", "test message", "TestKind");
            var fromLocation = ErrorSiteTable.CreateSite(model.GetReportLocation(node), "TestMember",
                "test message", "TestKind");

            Assert.Equal(fromNode.SiteId, fromLocation.SiteId);
            Assert.Equal(fromNode.File, fromLocation.File);
            Assert.Equal(fromNode.Line, fromLocation.Line);
            Assert.Equal(fromNode.Column, fromLocation.Column);
            Assert.Equal(fromNode.MemberName, fromLocation.MemberName);
            Assert.Equal(fromNode.DefaultMessage, fromLocation.DefaultMessage);

            var span = model.GetReportLocation(node).GetLineSpan();
            var expectedSiteId = ErrorSiteTable.ComputeSiteId(
                Path.GetFileName(span.Path),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1,
                span.EndLinePosition.Character + 1,
                "TestKind");
            Assert.Equal(expectedSiteId, fromLocation.SiteId);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Format_LocationOfANode_MatchesTheNodeOverload()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var binary = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().First();

        var fromNode = SourcePlace.Format(model, binary);
        var fromLocation = SourcePlace.Format(model.GetReportLocation(binary));

        Assert.Equal(fromNode, fromLocation);
        Assert.Equal(SourcePlace.Unknown, SourcePlace.Format(Location.None));

        var span = model.GetReportLocation(binary).GetLineSpan();
        var expectedPlace =
            $"{Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
        Assert.Equal(expectedPlace, fromLocation);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanSite_NoTable_WritesTheMessageAndThePlace()
    {
        var (trapCompilation, tree, model) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var binary = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().First();
        var location = model.GetReportLocation(binary);

        var site = ArrayNullCheck.PlanReadSite(location, trapCompilation, null, null);

        Assert.Equal(0u, site.SiteId);
        Assert.Equal(ArrayNullCheck.MessagePrefix + SourcePlace.Format(location), site.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanSite_WithTable_RegistersTheSourceMember()
    {
        var (trapCompilation, _, model) = RoslynTestUtility.GetTestCaseContext("Ir/IrTrapCases.cs");
        var method = IrTestCases.FindMethod(trapCompilation, "IrTrapCases.Traps", "DivideByAny");
        var tree = method.Locations[0].SourceTree!;
        var syntax = tree.GetRoot().FindNode(method.Locations[0].SourceSpan)
            .FirstAncestorOrSelf<MethodDeclarationSyntax>()!;
        var binary = syntax.DescendantNodes().OfType<BinaryExpressionSyntax>().First();
        var location = model.GetReportLocation(binary);

        var table = new ErrorSiteTable();
        var site = ArrayNullCheck.PlanReadSite(location, trapCompilation, table, null);

        Assert.NotEqual(0u, site.SiteId);
        Assert.Equal("", site.Message);
        Assert.Single(table.Entries);
        Assert.Equal("DivideByAny", table.Entries[0].MemberName);
    }
}
