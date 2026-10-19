#nullable enable
using System.Collections;
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class ArtifactHelperTests
{
    static ArtifactHelperTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch
            {
                // Ignore if already registered
            }
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GetCleanKernelFileName()
    {
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.g.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("C:/some/path/kernel.g.cu"));
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Test_ProcessArtifact_WithDefaultKey_EmitsWarning(string? envValue)
    {
        using var ctx = new ArtifactTestContext(envValue);

        Assert.True(ctx.ProcessArtifact());

        var resultData = ctx.ReadResult();
        Assert.Equal(3 + Ed25519.SignatureSize, resultData.Length);
        Assert.Single(ctx.BuildEngine.LoggedWarnings);
        Assert.Contains("default G# development key", ctx.BuildEngine.LoggedWarnings[0]);
        Assert.Contains(ArtifactHelper.DefaultSigningKeyWarningCode, ctx.BuildEngine.LoggedWarningCodes);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ProcessArtifact_WithHexKey()
    {
        // 32-byte hex key = 64 characters
        var hexKey = new string('A', 64);
        using var ctx = new ArtifactTestContext(hexKey);

        Assert.True(ctx.ProcessArtifact());

        var resultData = ctx.ReadResult();
        Assert.Equal(3 + Ed25519.SignatureSize, resultData.Length);
        Assert.Empty(ctx.BuildEngine.LoggedWarnings);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ProcessArtifact_WithBase64Key()
    {
        // 32-byte base64 key
        var base64Key = Convert.ToBase64String(new byte[32]);
        using var ctx = new ArtifactTestContext(base64Key);

        Assert.True(ctx.ProcessArtifact());

        var resultData = ctx.ReadResult();
        Assert.Equal(3 + Ed25519.SignatureSize, resultData.Length);
        Assert.Empty(ctx.BuildEngine.LoggedWarnings);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("invalid_length_key")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000g")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Test_ProcessArtifact_WithInvalidKey_ReportsGS0003AndLeavesFileUnchanged(string envValue)
    {
        using var ctx = new ArtifactTestContext(envValue);

        Assert.False(ctx.ProcessArtifact());

        Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Equal(ArtifactHelper.InvalidSigningKeyErrorCode, Assert.Single(ctx.BuildEngine.LoggedErrorCodes));
        Assert.DoesNotContain(envValue, ctx.BuildEngine.LoggedErrors[0]);
        Assert.Empty(ctx.BuildEngine.LoggedWarnings);
        Assert.Equal(new byte[] { 1, 2, 3 }, ctx.ReadResult());
    }

    private sealed class ArtifactTestContext : IDisposable
    {
        private readonly string? _prevEnv;

        public string TempFile { get; }
        public MockBuildEngine BuildEngine { get; } = new();
        public TaskLoggingHelper Log { get; }

        public ArtifactTestContext(string? signingKeyEnv, byte[]? initialContent = null)
        {
            _prevEnv = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", signingKeyEnv);
            TempFile = Path.GetTempFileName();
            File.WriteAllBytes(TempFile, initialContent ?? [1, 2, 3]);
            Log = new TaskLoggingHelper(BuildEngine, "MockTask");
        }

        public bool ProcessArtifact() => ArtifactHelper.ProcessArtifact(TempFile, Log);

        public byte[] ReadResult() => File.ReadAllBytes(TempFile);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", _prevEnv);
            if (File.Exists(TempFile))
            {
                File.Delete(TempFile);
            }
        }
    }

    private sealed class MockBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public List<string> LoggedWarnings { get; } = [];
        public List<string> LoggedWarningCodes { get; } = [];
        public List<string> LoggedErrors { get; } = [];
        public List<string> LoggedErrorCodes { get; } = [];

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            if (e.Message != null)
            {
                LoggedErrors.Add(e.Message);
            }

            if (e.Code != null)
            {
                LoggedErrorCodes.Add(e.Code);
            }
        }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            if (e.Message != null)
            {
                LoggedWarnings.Add(e.Message);
            }

            if (e.Code != null)
            {
                LoggedWarningCodes.Add(e.Code);
            }
        }
    }
}
