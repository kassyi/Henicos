#nullable enable
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Managed;

/// <summary>
/// Unit tests for <see cref="ManagedContextFields.PlanFieldName"/> and
/// <see cref="ManagedContextFields.PlanResumeLocalName"/> (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class ManagedFieldNameTests
{
    private const string Source = "class C { void M() { { int x = 1; } { int x = 2; } int @class = 0; } }";
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(Source);
    private static readonly SyntaxTree s_tree = s_compilation.SyntaxTrees.First();
    private static readonly SemanticModel s_model = s_compilation.GetSemanticModel(s_tree);

    private static readonly ISymbol s_x1 = GetLocalSymbol("x", 0);
    private static readonly ISymbol s_x2 = GetLocalSymbol("x", 1);
    private static readonly ISymbol s_class = GetLocalSymbol("class", 0);

    private static readonly StandardCppDialect s_cppDialect = new();

    private static string DistinctName(ISymbol symbol, int region) =>
        CppNameResolver.Resolve(symbol, symbol.Name, s_cppDialect, region).ToString();

    private static ISymbol GetLocalSymbol(string name, int index)
    {
        var declarators = s_tree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(d => d.Identifier.ValueText == name)
            .ToList();
        return s_model.GetDeclaredSymbol(declarators[index]) ??
               throw new InvalidOperationException($"Local symbol '{name}'[{index}] not found.");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanFieldName_NotShared_IsTheLocalName()
    {
        var result = ManagedContextFields.PlanFieldName("x", s_x1, 0, sharesName: false, "M");
        Assert.Equal(new ManagedFieldName("x", Renamed: false), result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanFieldName_KeywordName_IsEscaped()
    {
        var result = ManagedContextFields.PlanFieldName("class", s_class, 0, sharesName: false, "M");
        Assert.Equal(new ManagedFieldName("@class", Renamed: false), result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanFieldName_Shared_IsTheDistinctName()
    {
        var result = ManagedContextFields.PlanFieldName("x", s_x1, 0, sharesName: true, "M");
        Assert.Equal(new ManagedFieldName(DistinctName(s_x1, 0), Renamed: true), result);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void PlanFieldName_SharedWithoutASymbol_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ManagedContextFields.PlanFieldName("x", null, 0, sharesName: true, "M")
        );
        Assert.Contains("M", ex.Message);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanFieldName_SharedInAnotherRegion_DiffersByRegion()
    {
        var name0 = ManagedContextFields.PlanFieldName("x", s_x1, 0, sharesName: true, "M");
        var name1 = ManagedContextFields.PlanFieldName("x", s_x1, 1, sharesName: true, "M");
        Assert.NotEqual(name0.Name, name1.Name);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanResumeLocalName_DeclaredInBody_IsTheDistinctName()
    {
        var initial = new ManagedFieldName("x", Renamed: false);
        var result = ManagedContextFields.PlanResumeLocalName(initial, "x", s_x1, 0, declaredInBody: true);
        Assert.Equal(new ManagedFieldName(DistinctName(s_x1, 0), Renamed: true), result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanResumeLocalName_AlreadyRenamed_KeepsTheName()
    {
        var initial = new ManagedFieldName("n", Renamed: true);
        var result = ManagedContextFields.PlanResumeLocalName(initial, "x", s_x1, 0, declaredInBody: true);
        Assert.Equal(new ManagedFieldName("n", Renamed: true), result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanResumeLocalName_NotDeclaredInBody_KeepsTheName()
    {
        var initial = new ManagedFieldName("x", Renamed: false);
        var result = ManagedContextFields.PlanResumeLocalName(initial, "x", s_x1, 0, declaredInBody: false);
        Assert.Equal(new ManagedFieldName("x", Renamed: false), result);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void PlanResumeLocalName_NoSymbol_KeepsTheName()
    {
        var initial = new ManagedFieldName("x", Renamed: false);
        var result = ManagedContextFields.PlanResumeLocalName(initial, "x", null, 0, declaredInBody: true);
        Assert.Equal(new ManagedFieldName("x", Renamed: false), result);
    }
}
