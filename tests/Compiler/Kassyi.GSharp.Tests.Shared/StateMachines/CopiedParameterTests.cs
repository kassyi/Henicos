using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for the value parameters that a suspending device function copies into its frame
/// (<see cref="LoweredStateMachine.CopiedParameters"/>, #455) and for the references that <see cref="ContextHoister"/>
/// rewrites to the copy.
/// </summary>
[Category(TestCategory.Compiler)]
public class CopiedParameterTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("FieldAssignment", "v")]
    [InlineData("FieldCompoundAssignment", "v")]
    [InlineData("NestedFieldAssignment", "v")]
    [InlineData("InlineArrayElementAssignment", "v")]
    [InlineData("FixedBufferElementAssignment", "v")]
    [InlineData("FieldPrefixIncrement", "v")]
    [InlineData("FieldPostfixDecrement", "v")]
    [InlineData("FieldRefArgument", "v")]
    [InlineData("FieldOutArgument", "v")]
    [InlineData("TupleSwapFields", "v")]
    [InlineData("TupleSwapScalars", "a", "b")]
    [InlineData("NestedTupleAssignment", "v", "a")]
    [InlineData("MutatingMethodCall", "v")]
    [InlineData("NestedFieldMutatingMethodCall", "v")]
    public void Lower_WriteThroughValueParameter_CopiesParameter(string methodName, params string[] expected)
    {
        var lowered = LowerDeviceFunction("ValueParameterWrites.cs", methodName, withModel: true);

        Assert.Equal(expected, lowered.CopiedParameters);
    }

    // An array or a pointer refers to memory that the resumed call sees again, a Span<T> cannot live in the frame, and a
    // readonly method (or a method of a readonly struct) does not change its receiver
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ArrayElementAssignment")]
    [InlineData("ArrayFieldElementAssignment")]
    [InlineData("SpanElementAssignment")]
    [InlineData("PointerElementAssignment")]
    [InlineData("PointerFieldElementAssignment")]
    [InlineData("ReadOnlyMethodCall")]
    [InlineData("ReadOnlyStructMethodCall")]
    [InlineData("FieldReadOnly")]
    public void Lower_NoWriteIntoValueStorage_CopiesNothing(string methodName)
    {
        var lowered = LowerDeviceFunction("ValueParameterWrites.cs", methodName, withModel: true);

        Assert.Empty(lowered.CopiedParameters);
    }

    // Without a semantic model the leftmost identifier of the target is taken from the syntax, and every receiver of an
    // instance method call counts as written (a readonly method is copied too, which is safe)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("FieldAssignment")]
    [InlineData("NestedFieldAssignment")]
    [InlineData("InlineArrayElementAssignment")]
    [InlineData("FieldRefArgument")]
    [InlineData("TupleSwapFields")]
    [InlineData("MutatingMethodCall")]
    [InlineData("ReadOnlyMethodCall")]
    public void Lower_WriteThroughValueParameterWithoutModel_CopiesParameter(string methodName)
    {
        var lowered = LowerDeviceFunction("ValueParameterWrites.cs", methodName, withModel: false);

        Assert.Equal(["v"], lowered.CopiedParameters);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_CopiedFieldWrite_ReadsAndWritesTheFrameCopy()
    {
        var lowered = LowerDeviceFunction("ValueParameterWrites.cs", "FieldAssignment", withModel: true);

        var references = lowered.LoweredBody.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(static name => name.Identifier.ValueText == "v")
            .ToArray();
        Assert.Equal(2, references.Length);
        Assert.All(references, static name => Assert.True(IsContextMemberName(name)));
    }

    // An expansion can copy another method's body into the lowered method (an inlined iterator). A parameter of that
    // method with the name of a copied parameter is not the copied parameter and must stay a plain reference.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Lower_OtherMethodParameterWithCopiedName_IsNotRewritten()
    {
        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "ForeignParameterDemo.cs"));
        var methods = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        var owner = methods.Single(static m => m.Identifier.ValueText == "OwnerDevice");
        var other = methods.Single(static m => m.Identifier.ValueText == "Other");
        var ownerSymbol = model.GetDeclaredSymbol<IMethodSymbol>(owner)
                          ?? throw new InvalidOperationException("OwnerDevice has no symbol.");
        var ownerParameter = ownerSymbol.Parameters.Single();
        var otherParameter = (model.GetDeclaredSymbol<IMethodSymbol>(other)
                              ?? throw new InvalidOperationException("Other has no symbol.")).Parameters.Single();

        var ownerBody = LoweringAnnotations.AnnotateOriginalSpans(
            owner.Body ?? throw new InvalidOperationException("OwnerDevice has no block body."),
            compilation);
        var otherBody = LoweringAnnotations.AnnotateOriginalSpans(
            other.Body ?? throw new InvalidOperationException("Other has no block body."),
            compilation);

        // { v = v + 1; Gpu.SyncThreads(); int doubled = <Other's v> * 2; return v; }
        var combined = ownerBody.WithStatements(SyntaxFactory.List([
            ownerBody.Statements[0],
            ownerBody.Statements[1],
            otherBody.Statements[0],
            ownerBody.Statements[2]
        ]));

        var lowered = AstLoweringPipeline.Lower(combined, model, ownerSymbol);

        Assert.Equal(["v"], lowered.CopiedParameters);
        var references = lowered.LoweredBody.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(static name => name.Identifier.ValueText == "v")
            .ToArray();
        var contextReferences = references.Where(IsContextMemberName).ToArray();
        var plainReferences = references.Where(static name => !IsContextMemberName(name)).ToArray();

        Assert.Equal(3, contextReferences.Length);
        Assert.All(contextReferences, name => Assert.True(model.GetSymbol(name).IsEqualTo(ownerParameter)));
        var plain = Assert.Single(plainReferences);
        Assert.True(model.GetSymbol(plain).IsEqualTo(otherParameter));
    }

    // A body that the semantic model cannot bind (not in the compilation, without original-span annotations) still
    // copies a parameter whose name the write target starts at
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Lower_UnboundWriteTargetWithModel_CopiesParameterByName()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "ValueParameterWrites.cs"));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.ValueText == "FieldAssignment");
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException("FieldAssignment has no symbol.");
        var unbound = (BlockSyntax)SyntaxFactory.ParseStatement(
            (method.Body ?? throw new InvalidOperationException("FieldAssignment has no block body.")).ToFullString());

        var lowered = AstLoweringPipeline.Lower(unbound, model, symbol);

        Assert.Equal(["v"], lowered.CopiedParameters);
    }

    // The lowered method only reads its 'v'; the body copied in from another method writes that method's own 'v'
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Lower_OnlyOtherMethodWritesParameterWithSameName_CopiesNothing()
    {
        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "ForeignParameterDemo.cs"));
        var methods = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        var owner = methods.Single(static m => m.Identifier.ValueText == "ReadingOwner");
        var other = methods.Single(static m => m.Identifier.ValueText == "OtherWriter");
        var ownerSymbol = model.GetDeclaredSymbol<IMethodSymbol>(owner)
                          ?? throw new InvalidOperationException("ReadingOwner has no symbol.");

        var ownerBody = LoweringAnnotations.AnnotateOriginalSpans(
            owner.Body ?? throw new InvalidOperationException("ReadingOwner has no block body."),
            compilation);
        var otherBody = LoweringAnnotations.AnnotateOriginalSpans(
            other.Body ?? throw new InvalidOperationException("OtherWriter has no block body."),
            compilation);

        // { int read = v; Gpu.SyncThreads(); <OtherWriter's v> = <OtherWriter's v> * 2; return read + v; }
        var combined = ownerBody.WithStatements(SyntaxFactory.List([
            ownerBody.Statements[0],
            ownerBody.Statements[1],
            otherBody.Statements[0],
            ownerBody.Statements[2]
        ]));

        var lowered = AstLoweringPipeline.Lower(combined, model, ownerSymbol);

        Assert.Empty(lowered.CopiedParameters);
    }

    private static bool IsContextMemberName(IdentifierNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax access &&
        ContextLocalRewriter.IsContextPointerAccess(access) &&
        access.Name == name;

    private static LoweredStateMachine LowerDeviceFunction(string testCaseFile, string methodName, bool withModel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", testCaseFile));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{methodName} has no symbol.");

        return AstLoweringPipeline.Lower(method, withModel ? model : null, symbol);
    }
}
