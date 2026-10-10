#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class MemoryExtensionsTranspilerRuleTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_MemoryExtensionsTranspilerRule_HappyPath_TranspilesToThrust()
    {
        await VerifyGeneratorAsync();
    }
}
