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
    public Task Test_DeviceHeapIntrinsics_EmitGpuUnsafe()
    {
        var source = Emit(ReadBuiltIn("device_heap_intrinsics.yml"));

        return Verifier.Verify(source, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.Test_DeviceHeapIntrinsics_EmitGpuUnsafe");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DeviceHeapIntrinsics_KeepTheSignaturesAndMappingOfGpuUnsafe()
    {
        var source = Emit(ReadBuiltIn("device_heap_intrinsics.yml")).ReplaceLineEndings("\n");

        Assert.Contains(
            "[Kassyi.GSharp.Core.CudaIntrinsic(\"malloc\")]\n" +
            "public static unsafe void* Malloc(int size) => Kassyi.GSharp.Core.GpuOnlyException.Throw<nint>().ToPointer();",
            source);
        Assert.Contains(
            "[Kassyi.GSharp.Core.CudaIntrinsic(\"free\")]\n" +
            "public static unsafe void Free(void* ptr) => Kassyi.GSharp.Core.GpuOnlyException.Throw();",
            source);
        Assert.Contains(
            "/// <exception cref=\"GpuOnlyException\">Thrown when evaluated on the host CPU.</exception>\n" +
            "[Kassyi.GSharp.Core.CudaIntrinsic(\"free\")]",
            source);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_MergeByClass_JoinsTheBuiltInDefinitionsOfGpuIntoOne()
    {
        var ptx = Read(ReadBuiltIn("ptx_intrinsics.yml"));
        var heap = Read(ReadBuiltIn("device_heap_intrinsics.yml"));
        var driver = Read(ReadBuiltIn("cuda_driver_api.yml"));

        var merged = NativeIntrinsicEmitter.MergeByClass([ptx, driver, heap]);

        var gpu = Assert.Single(merged);
        Assert.Equal("Gpu", gpu.ClassName);
        Assert.Equal(ptx.Methods.Length + heap.Methods.Length, gpu.Methods.Length);
        Assert.Equal("Malloc", gpu.Methods[ptx.Methods.Length].Name);
        Assert.Equal("Gpu.Unsafe", Assert.Single(gpu.Classes).Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_MergeByClass_KeepsClassesOfOtherNamespacesApart()
    {
        var first = Read("namespace: A\nclassName: Gpu\nmethods:\n  - name: F\n");
        var second = Read("namespace: B\nclassName: Gpu\nmethods:\n  - name: G\n");

        var merged = NativeIntrinsicEmitter.MergeByClass([first, second]);

        Assert.Equal(["A", "B"], merged.Select(d => d.Namespace).ToArray());
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

    private static string EmitPtxIntrinsics() => Emit(ReadBuiltIn("ptx_intrinsics.yml"));

    private static string ReadBuiltIn(string fileName) =>
        File.ReadAllText(RepositoryPaths.Combine("src", "NativeApi", fileName));

    private static NativeApiDefinition Read(string yaml)
    {
        Assert.True(NativeApiYamlReader.TryRead(yaml, out var definition, out var error), error?.Message);
        return definition;
    }

    private static string Emit(string yaml)
    {
        var definition = Read(yaml);
        Assert.Equal(NativeApiBindingKind.Intrinsic, definition.BindingKind);

        return NativeIntrinsicEmitter.EmitIntrinsics(definition);
    }
}
