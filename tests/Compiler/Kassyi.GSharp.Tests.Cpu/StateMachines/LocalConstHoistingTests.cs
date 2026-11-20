using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// The local consts of a body that the CPU state machine splits are declared in front of its state switch, in the
/// order written, instead of becoming context fields, which are not constants. The managed output renames a const whose
/// name another declaration of the body has.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalConstHoistingTests
{
    private const string TestCase = "StateMachines/LocalConstLiftingCases.cu.cs";
    private const string KernelsType = "Kassyi.GSharp.Tests.Shared.TestCases.StateMachines.LocalConstLiftingCases";
    private const int ThreadCount = 4;

    private static readonly Lazy<(Compilation Output, Assembly? Assembly, string Errors)> s_managed =
        new(CompileManaged);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_ConstBeforeBarrier_DeclaresItInFrontOfTheSwitch()
    {
        var lowered = Lower("ConstBeforeBarrier");

        // mutant: HoistedVariableAnalysis.Analyze `if (IsConstDeclarator(decl))` -> `if (false)` -> D becomes a
        // context field
        // mutant: ContextHoister.VisitLocalDeclarationStatement without `return null;` -> D is declared twice
        Assert.Equal(["D"], FrontConstNames(lowered.LoweredBody));
        Assert.IsType<SwitchStatementSyntax>(lowered.LoweredBody.Statements.Last());
        Assert.Single(ConstDeclaratorNames(lowered.LoweredBody));
        Assert.DoesNotContain("D", lowered.ContextFields.Select(static f => f.Identifier.ValueText));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Lower_ConstsAndLocalFunction_KeepsConstsFirstInWrittenOrder()
    {
        var lowered = Lower("ConstsAndLocalFunction");

        // mutant: StateMachineLowerer.Lower adds LiftedConstants after the local functions -> F comes first
        var front = lowered.LoweredBody.Statements
            .TakeWhile(static s => s is not SwitchStatementSyntax)
            .SelectMany(static s => s switch
            {
                LocalDeclarationStatementSyntax { IsConst: true } constant =>
                    constant.Declaration.Variables.Select(static v => v.Identifier.ValueText),
                LocalFunctionStatementSyntax function => [function.Identifier.ValueText],
                _ => []
            })
            .ToArray();
        Assert.Equal(["A", "B", "F"], front);
        Assert.Equal(["t"], lowered.ContextFields.Select(static f => f.Identifier.ValueText));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Lower_ConstInsideLocalFunctionOrLambda_StaysInPlace()
    {
        var lowered = Lower("ConstInsideLocalFunctionAndLambda");

        // mutant: HoistedVariableAnalysis.Analyze `if (IsBodyLocal(decl))` -> `if (true)` -> K and L leave their
        // function and lambda for the front of the switch
        Assert.Empty(FrontConstNames(lowered.LoweredBody));
        var function = lowered.LoweredBody.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        Assert.Equal(["K"], ConstDeclaratorNames(function));
        var lambda = lowered.LoweredBody.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>()
            .Cast<SyntaxNode>()
            .Concat(lowered.LoweredBody.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>())
            .Single();
        Assert.Equal(["L"], ConstDeclaratorNames(lambda));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Lower_BodyWithoutSplitPoint_LeavesConstInPlace()
    {
        var lowered = Lower("ConstWithoutBarrier");

        // The single state keeps the declaration where it was written
        Assert.Empty(FrontConstNames(lowered.LoweredBody));
        var section = lowered.LoweredBody.DescendantNodes().OfType<SwitchSectionSyntax>().Single();
        Assert.Equal(["D"], ConstDeclaratorNames(section));
        Assert.Empty(lowered.ContextFields);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ContextLocalRewriter_SiblingConstsOfOneName_RenamesDeclarationsAndReferences()
    {
        var (output, _, errors) = s_managed.Value;
        Assert.Equal("", errors);

        var stateMachine = output.SyntaxTrees
            .SelectMany(static t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Single(static m => m.Identifier.ValueText.StartsWith("__gsharp_statemachine_", StringComparison.Ordinal) &&
                                m.Identifier.ValueText.EndsWith("__SiblingConstsOfOneName", StringComparison.Ordinal));

        // mutant: ContextLocalRewriter.CreateFrontDeclarationRenames.Rename `counts[name] < 2` -> `true` -> two consts
        // named K in one block (CS0128)
        // mutant: ContextLocalRewriter.VisitVariableDeclarator returns `visited` -> the references name a const that
        // is not declared (CS0103)
        Assert.Equal(["__gsharp_const_1_K", "__gsharp_const_2_K"], ConstDeclaratorNames(stateMachine));
        var references = stateMachine.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(static n => n.Identifier.ValueText)
            .Where(static n => n is "K" or "__gsharp_const_1_K" or "__gsharp_const_2_K")
            .ToArray();
        Assert.Equal(["__gsharp_const_1_K", "__gsharp_const_2_K"], references);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void IsNonValueIdentifierPosition_GotoCaseConstant_IsAValuePosition()
    {
        var (_, tree, _) = RoslynTestUtility.GetTestCaseContext(TestCase);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.ValueText == "GotoLabelAndGotoCase");
        var gotos = method.DescendantNodes().OfType<GotoStatementSyntax>().ToArray();

        // mutant: HoistedVariableAnalysis.IsNonValueIdentifierPosition `GotoStatementSyntax { RawKind: ... }` ->
        // `GotoStatementSyntax` -> the constant of goto case counts as a label name
        var gotoCase = gotos.Single(static g => g.IsKind(SyntaxKind.GotoCaseStatement));
        Assert.False(HoistedVariableAnalysis.IsNonValueIdentifierPosition(
            Assert.IsType<IdentifierNameSyntax>(gotoCase.Expression)));
        var gotoLabel = gotos.Single(static g => g.IsKind(SyntaxKind.GotoStatement));
        Assert.True(HoistedVariableAnalysis.IsNonValueIdentifierPosition(
            Assert.IsType<IdentifierNameSyntax>(gotoLabel.Expression)));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedStateMachine_CaseLabelConstAfterBarrier_MatchesTheConstCase()
    {
        int[] r = [-1, -1, -1, -1];

        Invoke("RunCaseLabelConstAcrossBarrier", r);

        Assert.Equal([0, 0, 1, 0], r);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ManagedStateMachine_GotoCaseRenamedConst_JumpsToTheConstCase()
    {
        int[] r = new int[ThreadCount];

        Invoke("RunGotoCaseRenamedConst", r);

        Assert.Equal([5, 5, 5, 5], r);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ManagedStateMachine_ConstPatternsAfterBarrier_MatchTheConst()
    {
        int[] r = new int[ThreadCount * 3];

        Invoke("RunConstPatternsAcrossBarrier", r);

        // case < Lim, t is < Lim, t is Lim for t = 0..3
        Assert.Equal([1, 1, 8, 2, 2, 7, 2, 2, 8, 2, 2, 8], r);
    }

    private static string[] FrontConstNames(BlockSyntax body) =>
        body.Statements
            .TakeWhile(static s => s is not SwitchStatementSyntax)
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(static d => d.IsConst)
            .SelectMany(static d => d.Declaration.Variables)
            .Select(static v => v.Identifier.ValueText)
            .ToArray();

    private static string[] ConstDeclaratorNames(SyntaxNode node) =>
        node.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(static d => d.IsConst)
            .SelectMany(static d => d.Declaration.Variables)
            .Select(static v => v.Identifier.ValueText)
            .ToArray();

    private static void Invoke(string run, int[] r)
    {
        var (_, assembly, errors) = s_managed.Value;
        Assert.True(assembly is not null, errors);
        var kernels = assembly.GetType(KernelsType, throwOnError: true);
        Assert.NotNull(kernels);
        var method = kernels.GetMethod(run, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            method.Invoke(null, [r, (uint)ThreadCount]);
        }
    }

    // The generated managed C# of the test case, built in memory
    private static (Compilation Output, Assembly? Assembly, string Errors) CompileManaged()
    {
        var (_, output, _) = GeneratorTestRunner.RunGenerator(RoslynTestUtility.ReadTestCase(TestCase), "Cpu");
        using var stream = new MemoryStream();
        var result = output.Emit(stream);
        var errors = string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        if (!result.Success)
        {
            return (output, null, errors);
        }

        stream.Position = 0;
        var assembly = new AssemblyLoadContext($"LocalConstLifting_{Guid.NewGuid():N}", isCollectible: true)
            .LoadFromStream(stream);
        return (output, assembly, errors);
    }

    private static LoweredStateMachine Lower(string methodName)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(TestCase);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);
        return AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
    }
}
