using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class HoistingTests
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Hoist_VariablesAcrossBarrier()
    {
        var code = """
        {
            int a = 1;
            int b = 2; // Conservatively hoisted because its syntactic block overlaps the barrier.
            Gpu.SyncThreads();
            int c = a + 3; // 'a' is hoisted (accessed via ctx->a), 'c' is kept local.
        }
        """;
        var block = (BlockSyntax)SyntaxFactory.ParseStatement(code);
        
        var hoister = new ContextHoister();
        var (rewrittenBlock, hoistedDecls) = hoister.Hoist(block);
        
        var lowerer = new StateMachineLowerer();
        var loweredBlock = lowerer.Lower(rewrittenBlock);

        var formatted = loweredBlock.NormalizeWhitespace().ToFullString();
        return VerifyXunit.Verifier.Verify(formatted);
    }
}
