using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Extensions;

[Category(TestCategory.Compiler)]
public class SemanticModelExtensionsTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CrossTreeAnnotation_WithTreeOrdinal_ResolvesSymbolsAndLocationsInTargetTree()
    {
        var source0 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree0.cs");
        var source1 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree1.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source0, source1);

        var trees = compilation.SyntaxTrees.ToArray();
        var tree0 = trees[^2];
        var tree1 = trees[^1];
        var model0 = compilation.GetSemanticModel(tree0);

        var computeMethod = tree1.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(static m => m.Identifier.ValueText == "Compute");

        // Annotate computeMethod's body with tree1's ordinal and detach it into a fresh synthetic tree
        var annotatedBody = LoweringAnnotations.AnnotateOriginalSpans(
            computeMethod.Body ?? throw new InvalidOperationException("Compute has a block body"),
            compilation
        );
        var detachedWrapper = SyntaxFactory.Block(annotatedBody.Statements);

        Assert.NotSame(tree0, detachedWrapper.SyntaxTree);
        Assert.NotSame(tree1, detachedWrapper.SyntaxTree);

        var detachedDeclarator = detachedWrapper.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(static v => v.Identifier.ValueText == "factor");
        var detachedInvocation = detachedWrapper.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First();
        var detachedStepRef = detachedWrapper.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(static id => id.Identifier.ValueText == "Step");

        // 1. GetOriginalNode resolves to tree1
        var originalNode = model0.GetOriginalNode(detachedInvocation);
        Assert.NotNull(originalNode);
        Assert.Same(tree1, originalNode.SyntaxTree);
        Assert.Equal("Helper(input)", originalNode.ToString());

        // 2. GetSymbol resolves TreeOneProvider.Helper from model0
        var helperSymbol = Assert.IsAssignableFrom<IMethodSymbol>(model0.GetSymbol(detachedInvocation));
        Assert.Equal("Helper", helperSymbol.Name);
        Assert.Equal("TreeOneProvider", helperSymbol.ContainingType.Name);

        // 3. GetTypeSymbol resolves int on the invocation in tree1 from model0
        var typeSymbol = model0.GetTypeSymbol(detachedInvocation);
        Assert.NotNull(typeSymbol);
        Assert.Equal(SpecialType.System_Int32, typeSymbol.SpecialType);

        // 4. GetDeclaredSymbol resolves the local 'factor' declared in tree1 from model0
        var localSymbol = model0.GetDeclaredSymbol<ILocalSymbol>(detachedDeclarator);
        Assert.NotNull(localSymbol);
        Assert.Equal("factor", localSymbol.Name);

        // 5. GetSafeConstantValue and GetSafeOperation resolve against tree1's semantic model
        var constVal = model0.GetSafeConstantValue(detachedStepRef);
        Assert.True(constVal.HasValue);
        Assert.Equal(7, constVal.Value);

        var operation = model0.GetSafeOperation(detachedInvocation);
        Assert.IsAssignableFrom<IInvocationOperation>(operation);

        // 6. GetOriginalLocation and GetReportLocation return locations in tree1
        var originalLocation = model0.GetOriginalLocation(detachedInvocation);
        Assert.NotNull(originalLocation);
        Assert.Same(tree1, originalLocation.SourceTree);

        var reportLocation = model0.GetReportLocation(detachedInvocation);
        Assert.Same(tree1, reportLocation.SourceTree);
        Assert.Equal(originalNode.Span, reportLocation.SourceSpan);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TwoFieldAnnotation_WithoutTreeOrdinal_ResolvesAgainstReceiverSemanticModelTree()
    {
        var source0 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree0.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source0);
        var tree0 = compilation.SyntaxTrees.Last();
        var model0 = compilation.GetSemanticModel(tree0);

        var consumeMethod = tree0.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(static m => m.Identifier.ValueText == "Consume");

        // Annotate without compilation (2-field "start,length" format)
        var annotatedBody = LoweringAnnotations.AnnotateOriginalSpans(
            consumeMethod.Body ?? throw new InvalidOperationException("Consume has a block body")
        );
        var detachedWrapper = SyntaxFactory.Block(annotatedBody.Statements);

        var detachedDeclarator = detachedWrapper.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(static v => v.Identifier.ValueText == "localZero");
        var detachedBinaryExpr = detachedWrapper.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        var localSymbol = model0.GetDeclaredSymbol<ILocalSymbol>(detachedDeclarator);
        Assert.NotNull(localSymbol);
        Assert.Equal("localZero", localSymbol.Name);

        var exprType = model0.GetTypeSymbol(detachedBinaryExpr);
        Assert.NotNull(exprType);
        Assert.Equal(SpecialType.System_Int32, exprType.SpecialType);

        var reportLocation = model0.GetReportLocation(detachedBinaryExpr);
        Assert.Same(tree0, reportLocation.SourceTree);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("10,5", 10, 5, -1, 0)]
    [InlineData("10,5,2", 10, 5, 2, 0)]
    [InlineData("10,5,2,3", 10, 5, 2, 3)]
    [InlineData("10,5,-1,4", 10, 5, -1, 4)]
    public void TryParseOriginalSpanData_And_FormatOriginalSpanData_RoundTrip(
        string input,
        int expectedStart,
        int expectedLength,
        int expectedTreeOrdinal,
        int expectedRegion)
    {
        Assert.True(LoweringAnnotations.TryParseOriginalSpanData(
            input,
            out var start,
            out var length,
            out var treeOrdinal,
            out var region));
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedLength, length);
        Assert.Equal(expectedTreeOrdinal, treeOrdinal);
        Assert.Equal(expectedRegion, region);

        var formatted = LoweringAnnotations.FormatOriginalSpanData(start, length, treeOrdinal, region);
        Assert.Equal(input, formatted);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FourFieldAnnotation_WithRegion_ResolvesIdenticallyToThreeFieldAnnotation()
    {
        var source0 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree0.cs");
        var source1 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree1.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source0, source1);

        var trees = compilation.SyntaxTrees.ToArray();
        var tree0 = trees[^2];
        var tree1 = trees[^1];
        var model0 = compilation.GetSemanticModel(tree0);

        var computeMethod = tree1.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(static m => m.Identifier.ValueText == "Compute");

        var annotatedBody = LoweringAnnotations.AnnotateOriginalSpans(
            computeMethod.Body ?? throw new InvalidOperationException("Compute has a block body"),
            compilation
        );
        var stampedBody = LoweringAnnotations.StampRegion(annotatedBody, region: 5, model0);
        var detachedWrapper = SyntaxFactory.Block(stampedBody.Statements);

        var detachedDeclarator = detachedWrapper.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(static v => v.Identifier.ValueText == "factor");
        var detachedInvocation = detachedWrapper.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First();

        Assert.Equal(5, LoweringIdentity.GetRegion(detachedDeclarator));

        var originalNode = model0.GetOriginalNode(detachedInvocation);
        Assert.NotNull(originalNode);
        Assert.Same(tree1, originalNode.SyntaxTree);
        Assert.Equal("Helper(input)", originalNode.ToString());

        var localSymbol = model0.GetDeclaredSymbol<ILocalSymbol>(detachedDeclarator);
        Assert.NotNull(localSymbol);
        Assert.Equal("factor", localSymbol.Name);

        var reportLocation = model0.GetReportLocation(detachedInvocation);
        Assert.Same(tree1, reportLocation.SourceTree);
        Assert.Equal(originalNode.Span, reportLocation.SourceSpan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StampRegion_NestedCopies_PreservesOuterRegionOnOuterReferencesAndReplacesSingleAnnotation()
    {
        var source0 = RoslynTestUtility.ReadTestCase("Extensions/SemanticModelExtensions_Tree0.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source0);
        var tree0 = compilation.SyntaxTrees.Last();
        var model0 = compilation.GetSemanticModel(tree0);

        var consumeMethod = tree0.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(static m => m.Identifier.ValueText == "Consume");
        var body = consumeMethod.Body ?? throw new InvalidOperationException("Consume has a block body");

        // Outer copy declares 'localZero'; stamp it with region 1.
        var annotatedOuter = LoweringAnnotations.AnnotateOriginalSpans(body, compilation);
        var outerStamped = LoweringAnnotations.StampRegion(annotatedOuter, region: 1, model0);

        // Inner copy declares 'value' (using the parameter 'value' span as its declaration syntax) and references
        // both 'localZero' (from the outer copy) and 'value' (declared in the inner copy).
        var valueParam = consumeMethod.ParameterList.Parameters[0];
        var annotatedValueParam = LoweringAnnotations.AnnotateOriginalSpans(valueParam, compilation);
        var outerLocalZeroRef = outerStamped.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(static id => id.Identifier.ValueText == "localZero");
        var valueRefInBody = annotatedOuter.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(static id => id.Identifier.ValueText == "value");

        var innerDeclarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("value"))
            .WithAdditionalAnnotations(annotatedValueParam.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
        var innerDeclStmt = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(innerDeclarator)));
        var innerExprStmt = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.AddExpression,
                outerLocalZeroRef,
                valueRefInBody));
        var innerBlock = SyntaxFactory.Block(innerDeclStmt, innerExprStmt);

        var innerStamped = LoweringAnnotations.StampRegion(innerBlock, region: 2, model0);

        var stampedInnerDecl = innerStamped.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single();
        var stampedRefs = innerStamped.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .ToArray();
        var stampedLocalZeroRef = stampedRefs.Single(static id => id.Identifier.ValueText == "localZero");
        var stampedValueRef = stampedRefs.Single(static id => id.Identifier.ValueText == "value");

        // 'value' was declared inside innerBlock -> region 2 on both its declarator and its reference.
        Assert.Equal(2, LoweringIdentity.GetRegion(stampedInnerDecl));
        Assert.Equal(2, LoweringIdentity.GetRegion(stampedValueRef));

        // 'localZero' was declared in the outer copy -> its reference inside innerStamped keeps region 1.
        Assert.Equal(1, LoweringIdentity.GetRegion(stampedLocalZeroRef));

        // Every node carries at most one OriginalSpanAnnotationKind annotation.
        Assert.All(
            innerStamped.DescendantNodesAndSelf(),
            static n => Assert.True(n.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind).Count() <= 1));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StampRegion_GenericLocalFunctionCall_StampsSimpleNameReference()
    {
        const string source = """
            namespace TestNamespace;
            public static class GenericLocalCaller
            {
                public static int Run(int x)
                {
                    T Id<T>(T v) => v;
                    return Id<int>(x);
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.ValueText == "Run");
        var body = method.Body ?? throw new InvalidOperationException("Run has a block body.");

        var annotated = LoweringAnnotations.AnnotateOriginalSpans(body, compilation);
        var stamped = LoweringAnnotations.StampRegion(annotated, region: 3, model);

        var localFuncDecl = stamped.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var genericCallName = stamped.DescendantNodes().OfType<GenericNameSyntax>().Single();

        Assert.Equal(3, LoweringIdentity.GetRegion(localFuncDecl));
        Assert.Equal(3, LoweringIdentity.GetRegion(genericCallName));
        Assert.Equal(
            LoweringIdentity.ForDeclaration(localFuncDecl, model),
            LoweringIdentity.ForReference(genericCallName, model));
    }
}
