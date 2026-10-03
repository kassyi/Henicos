using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public abstract class StateMachineTestBase
{
    protected static Task VerifyTransformation(string code, Func<BlockSyntax, SyntaxNode> transform)
    {
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        var transformed = transform(block);
        var formatted = transformed.NormalizeWhitespace().ToFullString();
        return Verifier.Verify(formatted);
    }

    protected static Task VerifyStateMachineLower(string code) =>
        VerifyTransformation(code, b => new StateMachineLowerer().Lower(b));

    protected static Task VerifyBranchRewrite(string code) =>
        VerifyTransformation(code, b => new BranchRewriter().Rewrite(b));

    protected static Task VerifyLoopRewrite(string code) =>
        VerifyTransformation(code, b => new LoopRewriter().Rewrite(b));

    protected static Task VerifyWarpRewrite(string code) =>
        VerifyTransformation(code, b => new WarpInstructionRewriter().Rewrite(b));

    protected static Task VerifyHoist(string code) =>
        VerifyTransformation(code, b =>
        {
            var (rewrittenBlock, _) = new ContextHoister().Hoist(b);
            return new StateMachineLowerer().Lower(rewrittenBlock);
        });
}
