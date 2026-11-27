using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// A warp primitive whose evaluation is conditional (right operand of a short-circuit operator) or repeated (loop
/// header) must keep that evaluation order after lowering, instead of being hoisted in front of the statement once.
/// </summary>
public class ConditionalEvaluationTests : StateMachineTestBase
{
    private const uint FullMask = 0xFFFFFFFFu;

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_OnRightOfAnd()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = flag && Warp.ShuffleSync(FullMask, value, 1) > 0;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_OnRightOfOr()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = flag || Warp.ShuffleSync(FullMask, value, 1) > 0;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_InNestedShortCircuit()
    {
        bool first = false, second = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = first && (second || Warp.ShuffleSync(FullMask, value, 1) > 0);
        });
    }

    // The left operand is always evaluated, so plain hoisting is already correct
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_OnLeftOfAnd()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = Warp.ShuffleSync(FullMask, value, 1) > 0 && flag;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_InWhileCondition()
    {
        int count = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            while (Warp.ShuffleSync(FullMask, count, 0) < limit)
            {
                count++;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_InForIncrementor()
    {
        int steps = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            for (int i = 0; i < limit; i = Warp.ShuffleSync(FullMask, i + 1, 0))
            {
                steps++;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_InDoWhileCondition()
    {
        int count = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            do
            {
                count++;
            } while (Warp.ShuffleSync(FullMask, count, 0) < limit);
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task ConditionalRewriter_UserDefinedAndWithSplit_LowersToTruthTestAndBitwiseAnd() =>
        VerifySharedKernelConditionalLower("AndMaskKernel");

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task ConditionalRewriter_UserDefinedOrWithSplit_LowersToTruthTestAndBitwiseOr() =>
        VerifySharedKernelConditionalLower("OrMaskKernel");

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task ConditionalRewriter_BuiltInBoolWithSemanticModel_KeepsBuiltInForm()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public static class BuiltInBoolCases
            {
                public static void Both(bool flag, int value)
                {
                    bool okAnd = flag && Warp.ShuffleSync(0xFFFFFFFFu, value, 1) > 0;
                    bool okOr = flag || Warp.ShuffleSync(0xFFFFFFFFu, value, 1) > 0;
                }
            }
            """;
        return VerifyInlineConditionalLower(Source, "Both");
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task ConditionalRewriter_LiftedUserDefinedAndWithSplit_KeepsBuiltInForm()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public struct LiftedTruth
            {
                public int Value;
                public static LiftedTruth operator &(LiftedTruth left, LiftedTruth right) => left;
                public static bool operator true(LiftedTruth value) => value.Value > 0;
                public static bool operator false(LiftedTruth value) => value.Value < 0;
                public static bool operator true(LiftedTruth? value) => value is { Value: > 0 };
                public static bool operator false(LiftedTruth? value) => value is { Value: < 0 };
            }

            public static class LiftedTruthCases
            {
                public static void LiftedAnd(LiftedTruth? left, LiftedTruth? right)
                {
                    LiftedTruth? result = default;
                    result = left && (Warp.ActiveMask() != 0u ? right : null);
                }
            }
            """;
        return VerifyInlineConditionalLower(Source, "LiftedAnd");
    }

    private static Task VerifySharedKernelConditionalLower(
        string kernelName,
        [CallerMemberName] string testName = "")
    {
        string kernelPath = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Syntax", "OperatorMacroConditionalSplitKernels.cu.cs");
        (Compilation compilation, SyntaxTree tree) = RoslynTestUtility.GetCallerSourceContext(kernelPath);
        return VerifyConditionalLower(compilation, tree, kernelName, testName);
    }

    private static Task VerifyInlineConditionalLower(
        string source,
        string methodName,
        [CallerMemberName] string testName = "")
    {
        CSharpCompilation compilation = RoslynTestUtility.CreateCompilation(source);
        SyntaxTree tree = compilation.SyntaxTrees.Last();
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        return VerifyConditionalLower(compilation, tree, methodName, testName);
    }

    private static Task VerifyConditionalLower(
        Compilation compilation,
        SyntaxTree tree,
        string methodName,
        string testName)
    {
        SemanticModel model = compilation.GetSemanticModel(tree);
        MethodDeclarationSyntax method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        BlockSyntax body = method.Body ?? throw new System.InvalidOperationException($"{methodName} has no body.");
        BlockSyntax prepared = AstLoweringPipeline.Prepare(body, returnsValue: false, model);
        BlockSyntax rewritten = new ConditionalEvaluationRewriter(new LoweringTempNames(), model).Rewrite(prepared);
#pragma warning disable RS0030 // The .verified.cs snapshot is the text of the lowered tree
        string formatted = rewritten.NormalizeWhitespace().ToFullString();
#pragma warning restore RS0030
        string snapshot = $$"""
            public class {{testName}}_Snapshot
            {
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(snapshot, "cs")
            .UseFastHashSnapshotName(testName);
    }
}
