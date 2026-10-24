using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
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

    protected static LoweredIterator LowerIteratorFromDelegate(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "")
    {
        var lambda = (ParenthesizedLambdaExpressionSyntax)SyntaxFactory.ParseExpression(source);
        var block = (BlockSyntax)lambda.Body;
        if (block.Statements is [LocalFunctionStatementSyntax { Body: { } iteratorBody } localFunction])
        {
            return AstLoweringPipeline.LowerIterator(iteratorBody, localFunction.ParameterList);
        }

        return AstLoweringPipeline.LowerIterator(block);
    }

    protected static Task VerifyIteratorLower(Action action,
        [CallerArgumentExpression(nameof(action))]
        string source = "", [CallerMemberName] string methodName = "") =>
        VerifyTransformation(action, b =>
        {
            if (b.Statements is [LocalFunctionStatementSyntax { Body: { } iteratorBody } localFunction])
            {
                return AstLoweringPipeline.LowerIterator(iteratorBody, localFunction.ParameterList).MoveNextBody;
            }

            return AstLoweringPipeline.LowerIterator(b).MoveNextBody;
        }, source, methodName);
}

// Mock class to allow compiling test delegates containing Gpu.* calls without depending on the actual Intrinsics.
public static class Gpu
{
    public static void SyncThreads()
    {
    }

    public static void SyncWarp()
    {
    }
}

// Mock of the warp intrinsics with the argument layout the state machine lowering expects (mask, value, lane)
public static class Warp
{
    public static int ShuffleSync(uint mask, int value, int srcLane) => value;
    public static int ShuffleXorSync(uint mask, int value, int laneMask) => value;
}
