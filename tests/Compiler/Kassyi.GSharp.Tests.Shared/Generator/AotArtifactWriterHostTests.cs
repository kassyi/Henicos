using Kassyi.GSharp.Compiler.Aot;

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
}
