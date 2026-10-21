#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class MacroTranspilerTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public async Task Test_MacroTranspiler_ExpandsMacrosCorrectly_Snapshots()
    {
        await VerifyGeneratorAsync();
    }
}

