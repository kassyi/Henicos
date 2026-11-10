#nullable enable
#pragma warning disable CS0649, CS8656
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The copy of the receiver that a read of a getter which may change its struct is called on (#610):
/// <c>ComputedPropertyGetterCall.PlanCopyType</c>, without C++.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ComputedPropertyCopyPlanTests
{
    // The member access or name that reads the property in the body of the named member of this file
    private static (SyntaxNode Node, ISymbol? Symbol, SemanticModel Model) FindRead(
        string memberName,
        string propertyName,
        [CallerFilePath] string callerFilePath = "")
    {
        var (compilation, tree) = MegaCompilationCache.GetSourceContext(callerFilePath);
        var model = compilation.GetSemanticModel(tree);
        var member = tree.GetRoot()
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .First(m => (m is MethodDeclarationSyntax { Identifier.ValueText: var name } && name == memberName) ||
                        (m is PropertyDeclarationSyntax { Identifier.ValueText: var p } && p == memberName));
        var node = member.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .First(n => n.Identifier.ValueText == propertyName);
        SyntaxNode read = node.Parent is MemberAccessExpressionSyntax access && access.Name == node ? access : node;
        return (read, model.GetSymbolInfo(read).Symbol, model);
    }

    private static INamedTypeSymbol GetType(SemanticModel model, string name) =>
        model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(t => model.GetDeclaredSymbol(t))
            .OfType<INamedTypeSymbol>()
            .First(t => t.Name == name);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanCopyType_InParameter_CopiesTheStruct()
    {
        var (node, symbol, model) = FindRead(nameof(CopyPlanCases.ReadIn), nameof(CopyPlanCounter.Next));

        var copy = ComputedPropertyGetterCall.PlanCopyType(node, symbol, model, new StandardCppDialect(), null);

        Assert.Equal(nameof(CopyPlanCounter), copy?.Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanCopyType_LocalOrConstGetter_CallsOnTheReceiver()
    {
        var (local, localSymbol, model) = FindRead(nameof(CopyPlanCases.ReadLocal), nameof(CopyPlanCounter.Next));
        var (constant, constSymbol, _) = FindRead(nameof(CopyPlanCases.ReadConstIn), nameof(CopyPlanCounter.Doubled));
        var dialect = new StandardCppDialect();

        Assert.Null(ComputedPropertyGetterCall.PlanCopyType(local, localSymbol, model, dialect, null));
        Assert.Null(ComputedPropertyGetterCall.PlanCopyType(constant, constSymbol, model, dialect, null));
    }

    // Inside a readonly member of a generic struct, the copy is of the struct being defined, not of the open Box<T>
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanCopyType_ReadonlyMemberOfGenericStruct_SubstitutesTypeArguments()
    {
        var (node, symbol, model) = FindRead("Peek", "Next");
        var box = GetType(model, "CopyPlanBox");
        var boxOfFloat = box.Construct(model.Compilation.GetSpecialType(SpecialType.System_Single));

        var copy = ComputedPropertyGetterCall.PlanCopyType(node, symbol, model, new StandardCppDialect(), boxOfFloat);

        Assert.NotNull(copy);
        Assert.True(SymbolEqualityComparer.Default.Equals(boxOfFloat, copy));
    }
}

public struct CopyPlanCounter
{
    public int N;

    public int Next
    {
        get
        {
            N++;
            return N;
        }
    }

    public int Doubled => N * 2;
}

public struct CopyPlanBox<T>
    where T : unmanaged
{
    public int N;
    public T Tag;

    public int Next
    {
        get
        {
            N++;
            return N;
        }
    }

    public readonly int Peek => Next;
}

public static class CopyPlanCases
{
    public static int ReadIn(in CopyPlanCounter c) => c.Next;

    public static int ReadConstIn(in CopyPlanCounter c) => c.Doubled;

    public static int ReadLocal()
    {
        var c = new CopyPlanCounter();
        return c.Next;
    }
}
