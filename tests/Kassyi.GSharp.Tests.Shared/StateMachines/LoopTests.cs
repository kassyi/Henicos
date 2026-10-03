using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class LoopTests : StateMachineTestBase
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_WhileStatement() => VerifyLoopRewrite("""
        {
            while (condition)
            {
                int a = 1;
                Gpu.SyncThreads();
            }
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_ForStatement_WithBreakAndContinue() =>
        // LoopRewriter itself does not flatten nested if-statements, so we should test its specific responsibilities:
        // rewriting loops and updating break/continue inside them.
        VerifyLoopRewrite("""
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == 5) continue;
                if (i == 8) break;
                Gpu.SyncThreads();
            }
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_DoStatement() => VerifyLoopRewrite("""
        {
            do
            {
                Gpu.SyncThreads();
            } while (condition);
        }
        """);
}
