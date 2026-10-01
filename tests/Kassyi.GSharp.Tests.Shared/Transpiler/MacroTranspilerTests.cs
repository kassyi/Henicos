#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class MacroTranspilerTests : TranspilerTestBase
{
    [Fact]
    [Trait("Category", "Transpiler")]
    [Trait("Category", "Syntax")]
    public async Task Test_MacroTranspiler_ExpandsMacrosCorrectly_Snapshots()
    {
        var config = TestGenerator("Transpiler/MacroTranspiler_InGpuKernel.cu.cs");
        var driver = await config.GetDriverAsync();

        await Verify(driver);
    }
}

