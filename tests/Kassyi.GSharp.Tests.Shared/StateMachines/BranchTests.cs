using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class BranchTests : StateMachineTestBase
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_IfStatement_WithoutElse() => VerifyBranchRewrite("""
        {
            if (condition)
            {
                int a = 1;
                Gpu.SyncThreads();
            }
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Lower_IfStatement_WithElse() => VerifyBranchRewrite("""
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
        """);
}
