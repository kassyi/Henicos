using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class StraightLineTests
{
    [Fact]
    [Trait("Category", "StateMachineLowering")]
    public Task Lower_StraightLine_TwoBarriers()
    {
        var code = @"
{
    int a = 1;
    Gpu.SyncThreads();
    int b = a + 2;
    Gpu.SyncThreads();
    int c = b * 3;
}";
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        
        var lowerer = new StateMachineLowerer();
        var loweredBlock = lowerer.Lower(block);
        
        var formatted = loweredBlock.NormalizeWhitespace().ToFullString();
        
        return Verify(formatted);
    }

    [Fact]
    [Trait("Category", "StateMachineLowering")]
    public Task Lower_StraightLine_NoBarriers()
    {
        var code = @"
{
    int a = 1;
    int b = a + 2;
}";
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        
        var lowerer = new StateMachineLowerer();
        var loweredBlock = lowerer.Lower(block);
        
        var formatted = loweredBlock.NormalizeWhitespace().ToFullString();
        
        return Verify(formatted);
    }
}
