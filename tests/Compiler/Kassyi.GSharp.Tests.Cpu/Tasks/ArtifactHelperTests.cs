#nullable enable
using System.Collections;
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class ArtifactHelperTests
{
    static ArtifactHelperTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void GetCleanKernelFileName_ValidKernelExtensionsAndPaths_StripsExtensionsAndDirectory()
    {
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.g.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("C:/some/path/kernel.g.cu"));
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Kassyi.GSharp.Kernels_Foo.g.cpp", "Kassyi.GSharp.Kernels_Foo", "Kassyi.GSharp.Kernels_Foo")]
    [InlineData("Kassyi.GSharp.Kernels_Foo.1.g.cpp", "Kassyi.GSharp.Kernels_Foo", "Kassyi.GSharp.Kernels_Foo.1")]
    [InlineData("Kassyi.GSharp.Kernels_Foo.0.g.cpp", "Kassyi.GSharp.Kernels_Foo", "Kassyi.GSharp.Kernels_Foo.0")]
    [InlineData("C:/out/A.B.C.15.g.cpp", "A.B.C", "A.B.C.15")]
    [InlineData("kernel1.cpp", "kernel1", "kernel1")]
    [InlineData("kernel2.cpp", "kernel2", "kernel2")]
    [InlineData("MyMod.extra.g.cpp", "MyMod.extra", "MyMod.extra")]
    [InlineData(".1.g.cpp", ".1", ".1")]
    [InlineData("foo..g.cpp", "foo.", "foo.")]
    [InlineData("foo.-1.g.cpp", "foo.-1", "foo.-1")]
    [InlineData("foo.99999999999.g.cpp", "foo.99999999999", "foo.99999999999")]
    public void GetNativeModuleAndUnitStem_VariousUnitPaths_GroupsOnlyTrailingNonNegativeInt(
        string sourcePath,
        string expectedModuleStem,
        string expectedUnitStem)
    {
        var (moduleStem, unitStem) = ArtifactHelper.GetNativeModuleAndUnitStem(sourcePath);
        Assert.Equal(expectedModuleStem, moduleStem);
        Assert.Equal(expectedUnitStem, unitStem);
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TrySignArtifact_DefaultKeyNullOrWhitespace_EmitsWarningAndSigns(string? envValue)
    {
        using var ctx = new ArtifactTestContext(envValue);

        Assert.True(ctx.TrySignArtifact());

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
    public void TrySignArtifact_ValidHexKey_SignsWithoutWarnings()
    {
        // 32-byte hex key = 64 characters
        var hexKey = new string('A', 64);
        using var ctx = new ArtifactTestContext(hexKey);

        Assert.True(ctx.TrySignArtifact());

        var resultData = ctx.ReadResult();
        Assert.Equal(3 + Ed25519.SignatureSize, resultData.Length);
        Assert.Empty(ctx.BuildEngine.LoggedWarnings);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void TrySignArtifact_ValidBase64Key_SignsWithoutWarnings()
    {
        // 32-byte base64 key
        var base64Key = Convert.ToBase64String(new byte[32]);
        using var ctx = new ArtifactTestContext(base64Key);

        Assert.True(ctx.TrySignArtifact());

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
    public void TrySignArtifact_InvalidKeyFormatOrLength_ReportsGS0003AndLeavesFileUnchanged(string envValue)
    {
        using var ctx = new ArtifactTestContext(envValue);

        Assert.False(ctx.TrySignArtifact());

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

        public bool TrySignArtifact() => ArtifactHelper.TrySignArtifact(TempFile, Log);

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
}
