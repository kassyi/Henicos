using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class AotArtifactWriterHostTests
{
    private const string NeutralBaseDir = @"C:\Program Files\dotnet\sdk\10.0.402\Roslyn\bincore\";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Roslyn.Worker", NeutralBaseDir)]
    [InlineData("JetBrains.Roslyn.Worker", NeutralBaseDir)]
    [InlineData("ServiceHub.RoslynCodeAnalysisService", NeutralBaseDir)]
    [InlineData("LanguageServer", NeutralBaseDir)]
    [InlineData("OmniSharp", NeutralBaseDir)]
    [InlineData("csc", @"C:\Users\x\AppData\Local\JetBrains\Rider\bin\")]
    [InlineData("Stryker.CLI", NeutralBaseDir)]
    [InlineData("csc", @"C:\Users\x\.nuget\packages\dotnet-stryker\5.0.0\tools\net10.0\any\")]
    public void IsToolHost_ToolProcess_ReturnsTrue(string domainName, string baseDir)
    {
        Assert.True(AotArtifactWriter.IsToolHost(domainName, baseDir));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("csc", NeutralBaseDir)]
    [InlineData("VBCSCompiler", NeutralBaseDir)]
    [InlineData("dotnet", @"C:\Program Files\dotnet\sdk\10.0.402\")]
    public void IsToolHost_BuildProcess_ReturnsFalse(string domainName, string baseDir)
    {
        Assert.False(AotArtifactWriter.IsToolHost(domainName, baseDir));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_IdenticalContent_PreservesTimestamps()
    {
        using var directory = new TemporaryDirectory();
        var artifacts = WriteAndAge(directory.Path, "// kernel body v1\n");

        Write(directory.Path, "// kernel body v1\n");

        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Source));
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.SymbolFile));
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Manifest));
    }

    // The first case has the length of the original body, so the content comparison, not the length, decides
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("// kernel body v2\n")]
    [InlineData("// kernel body v2 with different length\n")]
    public void Write_DifferentContent_UpdatesModifiedArtifacts(string updatedBody)
    {
        using var directory = new TemporaryDirectory();
        var artifacts = WriteAndAge(directory.Path, "// kernel body v1\n");

        Write(directory.Path, updatedBody);

        Assert.Contains(updatedBody, File.ReadAllText(artifacts.Source), StringComparison.Ordinal);
        Assert.True(File.GetLastWriteTimeUtc(artifacts.Source) > OldTime);
        Assert.True(File.GetLastWriteTimeUtc(artifacts.SymbolFile) > OldTime);
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Manifest));
    }

    private static readonly DateTime OldTime = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly record struct ArtifactPaths(string Source, string SymbolFile, string Manifest);

    private static void Write(string directory, string body) =>
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory),
            body,
            default
        );

    // Writes the artifacts of a CPU module and sets their write times back to OldTime
    private static ArtifactPaths WriteAndAge(string directory, string body)
    {
        Write(directory, body);
        var source = Path.Combine(directory, "Kassyi.GSharp.Kernels_TestModule.g.cpp");
        var artifacts = new ArtifactPaths(
            source,
            source + ".gsym.json",
            Path.Combine(directory, AotArtifactWriter.ManifestFileName)
        );
        File.SetLastWriteTimeUtc(artifacts.Source, OldTime);
        File.SetLastWriteTimeUtc(artifacts.SymbolFile, OldTime);
        File.SetLastWriteTimeUtc(artifacts.Manifest, OldTime);
        return artifacts;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
