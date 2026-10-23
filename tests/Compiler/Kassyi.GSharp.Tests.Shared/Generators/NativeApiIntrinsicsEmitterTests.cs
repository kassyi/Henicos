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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Test_GenericAndNestedDeclarations_EmitGenericMethodsAndNestedClasses()
    {
        var source = EmitGenericIntrinsics();

        return Verifier.Verify(source, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.Test_GenericAndNestedDeclarations_EmitGenericMethodsAndNestedClasses");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GenericAndNestedDeclarations_CompileWithTheirSupportingTypes()
    {
        var compilation = CreateGenericIntrinsicsCompilation();

        Assert.Empty(compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TypeTemplate_ReplacesOnlyTheWholePlaceholderIdentifier()
    {
        var source = EmitGenericIntrinsics().ReplaceLineEndings("\n");

        // T is replaced in TestFragment<TShape, T>; TShape keeps its name
        Assert.Contains(
            "public static TestFragment<TShape, float> Fill<TShape>(float value) where TShape : struct, ITestShape",
            source);
        Assert.Contains(
            "public static TestFragment<TShape, int> Fill<TShape>(int value) where TShape : struct, ITestShape",
            source);
    }

    /// <summary>
    /// Compiles the stubs of <c>TestCases/NativeApi/GenericIntrinsics.yml</c> with the kernels and supporting types of
    /// <c>TestCases/NativeApi/GenericIntrinsicCalls.cu.cs</c>.
    /// </summary>
    internal static Compilation CreateGenericIntrinsicsCompilation() =>
        RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/GenericIntrinsicCalls.cu.cs"),
            EmitGenericIntrinsics()
        );

    private static string EmitGenericIntrinsics() =>
        Emit(RoslynTestUtility.ReadTestCase("NativeApi/GenericIntrinsics.yml"));

    private static string EmitPtxIntrinsics() =>
        Emit(File.ReadAllText(RepositoryPaths.Combine("src", "NativeApi", "ptx_intrinsics.yml")));

    private static string Emit(string yaml)
    {
        Assert.True(NativeApiYamlReader.TryRead(yaml, out var definition, out var error), error?.Message);
        Assert.Equal(NativeApiBindingKind.Intrinsic, definition.BindingKind);

        return NativeIntrinsicEmitter.EmitIntrinsics(definition);
    }
}
