#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Shared.Helpers;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class MacroTranspilerTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Category(TestCategory.Syntax)]
    public async Task Test_MacroTranspiler_ExpandsMacrosCorrectly_Snapshots()
    {
        SkipIfCpuDialect("#626: the CPU dialects drop the body of Gpu.SyncGrid(body); the snapshot is refreshed with the fix");
        await VerifyGeneratorAsync();
    }
}

