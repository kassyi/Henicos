using System.IO;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Tests.Infrastructure;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiIntrinsicsEmitterTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Test_PtxIntrinsics_EmitsCudaIntrinsicStubs()
    {
        var source = EmitPtxIntrinsics();

        return Verifier.Verify(source, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.Test_PtxIntrinsics_EmitsCudaIntrinsicStubs");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PtxIntrinsics_AtomicAddInt_CarriesNativeNameAttribute()
    {
        var source = EmitPtxIntrinsics();

        Assert.Contains(
            "[Kassyi.GSharp.Core.CudaIntrinsic(\"atomicAdd\")]\npublic static int AtomicAdd(ref int address, int val)",
            source.ReplaceLineEndings("\n"));
    }

    private static string EmitPtxIntrinsics()
    {
        var path = RepositoryPaths.Combine("src", "NativeApi", "ptx_intrinsics.yml");
        Assert.True(
            NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error),
            error?.Message);
        Assert.Equal(NativeApiBindingKind.Intrinsic, definition.BindingKind);

        return NativeApiEmitter.EmitIntrinsics(definition);
    }
}
