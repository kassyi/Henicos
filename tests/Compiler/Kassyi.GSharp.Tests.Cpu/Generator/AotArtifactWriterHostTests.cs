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
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var target = new AotTarget("TestModule", false);
            var config = new GeneratorConfig("Cpu", false, dir);
            var sourcePath = Path.Combine(dir, "Kassyi.GSharp.Kernels_TestModule.g.cpp");
            var gsymPath = sourcePath + ".gsym.json";
            var manifestPath = Path.Combine(dir, AotArtifactWriter.ManifestFileName);
            var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            AotArtifactWriter.Write(target, config, "// kernel body v1\n", default);
            File.SetLastWriteTimeUtc(sourcePath, oldTime);
            File.SetLastWriteTimeUtc(gsymPath, oldTime);
            File.SetLastWriteTimeUtc(manifestPath, oldTime);

            AotArtifactWriter.Write(target, config, "// kernel body v1\n", default);

            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(sourcePath));
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(gsymPath));
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(manifestPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("// kernel body v2\n")]
    [InlineData("// kernel body v2 with different length\n")]
    public void Write_DifferentContent_UpdatesModifiedArtifacts(string updatedBody)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var target = new AotTarget("TestModule", false);
            var config = new GeneratorConfig("Cpu", false, dir);
            var sourcePath = Path.Combine(dir, "Kassyi.GSharp.Kernels_TestModule.g.cpp");
            var gsymPath = sourcePath + ".gsym.json";
            var manifestPath = Path.Combine(dir, AotArtifactWriter.ManifestFileName);
            var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            AotArtifactWriter.Write(target, config, "// kernel body v1\n", default);
            File.SetLastWriteTimeUtc(sourcePath, oldTime);
            File.SetLastWriteTimeUtc(gsymPath, oldTime);
            File.SetLastWriteTimeUtc(manifestPath, oldTime);

            AotArtifactWriter.Write(target, config, updatedBody, default);

            Assert.Contains(updatedBody, File.ReadAllText(sourcePath), StringComparison.Ordinal);
            Assert.True(File.GetLastWriteTimeUtc(sourcePath) > oldTime);
            Assert.True(File.GetLastWriteTimeUtc(gsymPath) > oldTime);
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(manifestPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
