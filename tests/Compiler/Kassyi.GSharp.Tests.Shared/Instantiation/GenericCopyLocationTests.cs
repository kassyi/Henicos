#nullable enable

using Kassyi.GSharp.Compiler.ErrorSites;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Where the closed copies of generic declarations are: every node of a copy names the node of the declaration it came
/// from, a diagnostic about it is reported there in the user's source, and the semantic questions about it (its
/// symbols, the scope of a position, the member around it) are answered by the copy in the derived compilation.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopyLocationTests
{
    // Diagnostics on a copy are reported at the user's source, so every node needs the place it came from; it is not
    // the original-span annotation, which the lowering would read the generic declaration's semantics through
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallsExt", "App.Odd")]
    [InlineData("CheckedNegate", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.Int16")]
    [InlineData("Identities", "System.Int32")]
    [InlineData("OneOverZero", "System.Int32")]
    [InlineData("CompoundDivide", "System.SByte")]
    [InlineData("MaxMagnitude", "System.Char")]
    [InlineData("Locals", "System.Int32")]
    public void Create_EveryNodeOfTheCopy_CarriesACopyOriginInsideTheDeclaration(string method, string typeArgument)
    {
        var (copy, _) = CreateCopy(method, typeArgument);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var original = DeclarationSpan(method);

        foreach (var node in declaration.DescendantNodesAndSelf())
        {
            Assert.False(node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind), $"{node.Kind()}");
            var annotation = Assert.Single(node.GetAnnotations(GenericCopyEmitter.CopyOriginAnnotationKind));
            Assert.True(LoweringAnnotations.TryParseOriginalSpanData(
                annotation.Data, out var start, out var length, out _, out _));
            Assert.True(original.Contains(new TextSpan(start, length)),
                $"{node.Kind()} points outside the declaration");
        }
    }

    // A diagnostic about a node of the copy is reported where the user wrote the node: in the tree of the user's
    // compilation, at the node of the declaration with the same node number
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Sum", "System.Int32")]
    [InlineData("Locals", "System.Int32")]
    [InlineData("CheckedNegate", "System.SByte")]
    public void Create_Copy_ReportLocationIsTheDeclarationNode(string method, string typeArgument)
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        var (copy, set) = CreateCopy(compilation, method, type);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var userTree = compilation.SyntaxTrees.Single();
        var userNodes = CopyNodeIndex.List(userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == method && m.TypeParameterList is not null));
        var model = ModelOf(set, declaration);

        var checkedNodes = 0;
        foreach (var node in declaration.DescendantNodes().OfType<ExpressionSyntax>())
        {
            if (!CopyNodeIndex.TryGet(node, out var index))
            {
                continue;
            }

            var location = model.GetReportLocation(node);
            Assert.Same(userTree, location.SourceTree);
            Assert.Equal(userNodes[index].Span, location.SourceSpan);
            checkedNodes++;
        }

        Assert.True(checkedNodes > 0);
    }

    // The copy of a function that waits at a barrier is lowered from its own symbols: the frame field that keeps 'kept'
    // across the wait has the type argument as its type, its local and its parameter are those of the copy (with the
    // annotation of the declaration they were T and the declaration's parameter: GSH0048, or a parameter left where a
    // frame field belongs), and the lowered nodes still report at the declaration
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_CopyThatReachesABarrier_LowersWithTheTypesOfTheCopy()
    {
        var compilation = CreateCompilation();
        var (copy, set) = CreateCopy(compilation, "AfterSync", compilation.GetSpecialType(SpecialType.System_Int32));
        Assert.True(copy.IsFaithful, Describe(copy));
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var model = ModelOf(set, declaration);

        var plan = MethodEmissionPlan.Create(declaration, model, isKernel: false);

        var lowered = Assert.IsType<LoweredStateMachine>(plan.Lowered);
        var kept = Assert.Single(
            lowered.ContextFields,
            f => f.Identifier.ValueText.Contains("kept", StringComparison.Ordinal));
        Assert.Equal(SpecialType.System_Int32, HoistedFieldTypeResolver.Resolve(kept, model, "AfterSync").SpecialType);
        // The lowered nodes resolve to the copy, not to the generic declaration: its local and its parameter
        var local = Assert.IsAssignableFrom<ILocalSymbol>(model.GetDeclaredSymbol<ISymbol>(kept));
        Assert.Equal(SpecialType.System_Int32, local.Type.SpecialType);
        var value = lowered.LoweredBody.DescendantNodes().OfType<IdentifierNameSyntax>()
            .First(static name => name.Identifier.ValueText == "value");
        Assert.Equal(copy.Symbol?.Parameters[0], model.GetSymbol(value), SymbolEqualityComparer.Default);
        var location = model.GetReportLocation(kept);
        Assert.Same(compilation.SyntaxTrees.Single(), location.SourceTree);
        Assert.True(DeclarationSpan("AfterSync").Contains(location.SourceSpan), location.ToString());
    }

    // The context of an operator macro is looked up in the scope of the copy, where its parameter is; the report
    // location of the assignment is in the user's source, which the derived compilation does not hold
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_CopyWithContextOperatorMacro_FindsTheContextParameterOfTheCopy()
    {
        var compilation = CreateCompilation();
        var (copy, set) = CreateCopy(compilation, "ContextMacro", compilation.GetSpecialType(SpecialType.System_Int32));
        Assert.True(copy.IsFaithful, Describe(copy));
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var model = ModelOf(set, declaration);
        var assignment = declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var target = set.Compilation.GetTypeByMetadataName("App.MacroTargets")?.GetMembers("Add")
                         .OfType<IMethodSymbol>()
                         .Single() ??
                     throw new InvalidOperationException("No MacroTargets.Add.");

        var found = OperatorMacroContext.TryFindContextVariable(assignment, target, model, out var variable,
            out var failure);

        Assert.True(found, failure);
        Assert.Equal(copy.Symbol?.Parameters[0], variable, SymbolEqualityComparer.Default);
    }

    // The copy of a [SkipLocalsInit] declaration does not zero its locals: the member around a node of the copy is the
    // copy in the derived compilation, not the declaration in the user's tree, which that compilation does not hold
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_CopyOfSkipLocalsInitDeclaration_DoesNotZeroItsLocals()
    {
        var (copy, set) = CreateCopy("SkipLocals", "System.Int32");
        Assert.True(copy.IsFaithful, Describe(copy));
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var local = declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();

        Assert.False(ModelOf(set, declaration).AreLocalsZeroedAt(local));
    }

    // An error site in a copy names the member as the user wrote it: the generic declaration, not the copy
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_CopyNode_SourceMemberNameIsTheDeclaration()
    {
        var (copy, set) = CreateCopy("Sum", "System.Int32");
        Assert.True(copy.IsFaithful, Describe(copy));
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var sum = declaration.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();

        Assert.Equal("Sum", ErrorSiteTable.GetSourceMemberName(sum, ModelOf(set, declaration)));
    }
}
