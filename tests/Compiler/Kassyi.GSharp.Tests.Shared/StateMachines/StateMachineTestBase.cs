#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public abstract class StateMachineTestBase
{
    protected static Task VerifyTransformation(Action action, Func<BlockSyntax, SyntaxNode> transform,
        [CallerArgumentExpression(nameof(action))]
        string source = "",
        [CallerMemberName] string methodName = "")
    {
        var lambda = (ParenthesizedLambdaExpressionSyntax)SyntaxFactory.ParseExpression(source);
        var block = (BlockSyntax)lambda.Body;
        var transformed = transform(block);
        var formatted = transformed.NormalizeWhitespace().ToFullString();

        var wrapped = $$"""
            public class {{methodName}}_Snapshot
            {
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(wrapped, "cs").UseFastHashSnapshotName(methodName);
    }

    protected static Task VerifyPipelineLowerFromTestCase(
        string testCaseFile,
        Func<BlockSyntax, SemanticModel, BlockSyntax>? preLowerTransform = null,
        Action<LoweredStateMachine, SemanticModel>? inspect = null,
        [CallerMemberName] string methodName = "")
    {
        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", testCaseFile));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        LoweredStateMachine lowered;
        if (preLowerTransform == null)
        {
            lowered = AstLoweringPipeline.Lower(method, model);
        }
        else
        {
            var body = method.Body ?? throw new InvalidOperationException("Test case method must have a block body.");
            var annotated = LoweringAnnotations.AnnotateOriginalSpans(body, compilation);
            var expanded = preLowerTransform(annotated, model);
            lowered = AstLoweringPipeline.Lower(expanded, model);
        }

        inspect?.Invoke(lowered, model);

        var dialect = new StandardCppDialect();
        var fieldsComment = string.Join(
            Environment.NewLine,
            lowered.ContextFields.Select(f =>
            {
                var symbol = model.GetDeclaredSymbol<ISymbol>(f);
                var region = LoweringIdentity.GetRegion(f);
                var cppName = CppNameResolver.Resolve(symbol, f.Identifier.ValueText, dialect, region);
                return $"    // {f.Identifier.ValueText} -> {cppName}";
            }));
        var formatted = lowered.LoweredBody.NormalizeWhitespace().ToFullString();

        var wrapped = $$"""
            public class {{methodName}}_Snapshot
            {
            {{fieldsComment}}
                public void LoweredMethod()
            {{formatted}}
            }
            """;
        return Verifier.Verify(wrapped, "cs").UseFastHashSnapshotName(methodName);
    }

    protected static Task VerifyStateMachineLower(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new StateMachineLowerer().Lower(b), source, methodName);

    protected static Task VerifyBranchRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new BranchRewriter().Rewrite(b), source, methodName);

    protected static Task VerifyLoopRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new LoopRewriter().Rewrite(b), source, methodName);

    protected static Task VerifyWarpRewrite(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "",
        [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => new WarpInstructionRewriter().Rewrite(b), source, methodName);

    // Without a semantic model only barriers and warp primitives written in the block split the state machine
    protected static Task VerifyPipelineLower(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b => AstLoweringPipeline.Lower(b).LoweredBody, source, methodName);

    protected static Task VerifyHoist(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b =>
        {
            var (rewrittenBlock, _) = new ContextHoister().Hoist(b);
            return new StateMachineLowerer().Lower(rewrittenBlock);
        }, source, methodName);
}

// Mock class to allow compiling test delegates containing Gpu.* calls without depending on the actual Intrinsics.
public static class Gpu
{
    public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

    public static void SyncThreads()
    {
    }

    public static void SyncWarp()
    {
    }

    public static void ForUnroll(int start, int count, Action<int> body)
    {
    }

    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body)
    {
    }
}

// Mock of the warp intrinsics with the argument layout the state machine lowering expects (mask, value, lane)
public static class Warp
{
    public static int ShuffleSync(uint mask, int value, int srcLane) => value;
    public static int ShuffleXorSync(uint mask, int value, int laneMask) => value;
}
