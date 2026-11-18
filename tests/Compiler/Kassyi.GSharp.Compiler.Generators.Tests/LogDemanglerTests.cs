#nullable enable

using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class LogDemanglerTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Demangle_SplitModuleSymbolFiles_DecodesHeaderShortenedAndUnitKernels()
    {
        using var scratch = new ScratchDirectory();
        const string ShortHelper = "__gsharp_fn_Helper_h1a2b3c4d";
        const string FullHelper = "__gsharp_fn_MyApp__Math__Helper";
        const string ShortKernel = "__gsharp_k_Run_h9e8d7c6b";
        const string FullKernel = "__gsharp_k_MyApp__Kernels__Run_g1_bSystem__Int32_e";

        var headerBody = $"inline int {ShortHelper}(int x) {{ return x + 1; }}\n";
        var unit0Body = "#include \"Kassyi.GSharp.Kernels_MyApp.g.hpp\"\nvoid __gsharp_k_MyApp__Init() {}\n";
        var unit1Body = $"#include \"Kassyi.GSharp.Kernels_MyApp.g.hpp\"\n"
            + $"void {ShortKernel}(int x) {{ {ShortHelper}(x); }}\n";
        var unit2Body = "#include \"Kassyi.GSharp.Kernels_MyApp.g.hpp\"\n";

        var errorSites = new[]
        {
            new GSharpNameEncoding.SymbolErrorSiteRecord(
                101u,
                "Kernels.cu.cs",
                12,
                5,
                "MyApp.Kernels.Run",
                "Index was outside the bounds of the array.")
        };

        WriteArtifactWithSymbolFile(
            scratch,
            "Kassyi.GSharp.Kernels_MyApp.g.hpp",
            headerBody,
            id => id == ShortHelper ? FullHelper : null,
            errorSites);
        WriteArtifactWithSymbolFile(
            scratch,
            "Kassyi.GSharp.Kernels_MyApp.g.cpp",
            unit0Body);
        WriteArtifactWithSymbolFile(
            scratch,
            "Kassyi.GSharp.Kernels_MyApp.1.g.cpp",
            unit1Body,
            id => id switch
            {
                ShortHelper => FullHelper,
                ShortKernel => FullKernel,
                _ => null
            });
        WriteArtifactWithSymbolFile(
            scratch,
            "Kassyi.GSharp.Kernels_MyApp.2.g.cpp",
            unit2Body);

        var log = $"error in {ShortKernel} calling {ShortHelper} and __gsharp_k_MyApp__Init";
        var demangled = LogDemangler.DemangleText(log, scratch.Path);

        Assert.Contains("MyApp.Kernels.Run<System.Int32>", demangled, StringComparison.Ordinal);
        Assert.Contains("MyApp.Math.Helper", demangled, StringComparison.Ordinal);
        Assert.Contains("MyApp.Init", demangled, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Demangle_MismatchedCompanionHash_ReturnsErrorExitCode()
    {
        using var scratch = new ScratchDirectory();
        var body = "#include \"Kassyi.GSharp.Kernels_MyApp.g.hpp\"\nvoid __gsharp_k_MyApp__Run__() {}\n";
        var unitPath = WriteArtifactWithSymbolFile(scratch, "Kassyi.GSharp.Kernels_MyApp.1.g.cpp", body);
        var logPath = scratch.Write("build.log", "undefined reference to __gsharp_k_MyApp__Run__");

        // Overwrite the companion source with a mismatched symbol header hash
        File.WriteAllText(unitPath, "// gsharp-symbols: 0000000000000000\n" + body);

        var result = GeneratorRun.Run("demangle", logPath, scratch.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("hash", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"broken_truncated_json\":")]
    [InlineData("{\"cpp\":\"__gsharp_k_Fake\",\"cs\":\"Fake.Wrong\"}")]
    [InlineData("")]
    [Scenario(TestScenario.SadPath)]
    public void Demangle_MalformedOrHeaderlessSymbolFileWithCompanionHash_ReturnsErrorExitCode(string invalidGsymContent)
    {
        using var scratch = new ScratchDirectory();
        var body = "#include \"Kassyi.GSharp.Kernels_MyApp.g.hpp\"\nvoid __gsharp_k_MyApp__Run__() {}\n";
        WriteArtifactWithSymbolFile(scratch, "Kassyi.GSharp.Kernels_MyApp.1.g.cpp", body);
        scratch.Write("Kassyi.GSharp.Kernels_MyApp.1.g.cpp.gsym.json", invalidGsymContent);
        var logPath = scratch.Write("build.log", "undefined reference to __gsharp_k_MyApp__Run__");

        var result = GeneratorRun.Run("demangle", logPath, scratch.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    private static string WriteArtifactWithSymbolFile(
        ScratchDirectory scratch,
        string fileName,
        string body,
        Func<string, string?>? resolveShortened = null,
        IReadOnlyList<GSharpNameEncoding.SymbolErrorSiteRecord>? errorSites = null)
    {
        var hash = GSharpNameEncoding.ComputeSourceHash(body);
        var sourceContent = GSharpNameEncoding.FormatSymbolHeaderLine(hash) + "\n" + body;
        var symbolContent = GSharpNameEncoding.BuildSymbolFileContent(body, "Cpu", resolveShortened, errorSites);
        var sourcePath = scratch.Write(fileName, sourceContent);
        scratch.Write(fileName + ".gsym.json", symbolContent);
        return sourcePath;
    }
}
