using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class BranchTests
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_IfStatement_WithoutElse()
    {
        var code = """
        {
            if (condition)
            {
                int a = 1;
                Gpu.SyncThreads();
            }
        }
        """;
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        
        var rewriter = new BranchRewriter();
        var rewritten = rewriter.Rewrite(block);
        
        var formatted = rewritten.NormalizeWhitespace().ToFullString();
        return VerifyXunit.Verifier.Verify(formatted);
    }

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_IfStatement_WithElse()
    {
        var code = """
        {
            if (condition)
            {
                int a = 1;
            }
            else
            {
                int b = 2;
                Gpu.SyncThreads();
            }
        }
        """;
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        
        var rewriter = new BranchRewriter();
        var rewritten = rewriter.Rewrite(block);
        
        var formatted = rewritten.NormalizeWhitespace().ToFullString();
        return VerifyXunit.Verifier.Verify(formatted);
    }
}
