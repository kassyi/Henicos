using System;
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

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

    private class MockBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public System.Collections.Generic.List<string> LoggedWarnings { get; } = new();

        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            LoggedWarnings.Add(e.Message);
        }
    }

    [Fact]
    public void Test_GetCleanKernelFileName()
    {
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("kernel.g.cu"));
        Assert.Equal("kernel", ArtifactHelper.GetCleanKernelFileName("C:/some/path/kernel.g.cu"));
    }

    [Fact]
    public void Test_ProcessArtifact_WithDefaultKey()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
        var tempFile = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", null);
            File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });

            var engine = new MockBuildEngine();
            var log = new TaskLoggingHelper(engine, "MockTask");

            Assert.True(ArtifactHelper.ProcessArtifact(tempFile, log));

            var resultData = File.ReadAllBytes(tempFile);
            Assert.Equal(3 + 64, resultData.Length); // 3 bytes original + 64 bytes signature
            Assert.Empty(engine.LoggedWarnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", prevEnv);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_ProcessArtifact_WithHexKey()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
        var tempFile = Path.GetTempFileName();
        try
        {
            // 32-byte hex key = 64 characters
            var hexKey = new string('A', 64);
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", hexKey);
            File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });

            var engine = new MockBuildEngine();
            var log = new TaskLoggingHelper(engine, "MockTask");

            Assert.True(ArtifactHelper.ProcessArtifact(tempFile, log));

            var resultData = File.ReadAllBytes(tempFile);
            Assert.Equal(3 + 64, resultData.Length);
            Assert.Empty(engine.LoggedWarnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", prevEnv);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_ProcessArtifact_WithBase64Key()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
        var tempFile = Path.GetTempFileName();
        try
        {
            // 32-byte base64 key
            var base64Key = Convert.ToBase64String(new byte[32]);
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", base64Key);
            File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });

            var engine = new MockBuildEngine();
            var log = new TaskLoggingHelper(engine, "MockTask");

            Assert.True(ArtifactHelper.ProcessArtifact(tempFile, log));

            var resultData = File.ReadAllBytes(tempFile);
            Assert.Equal(3 + 64, resultData.Length);
            Assert.Empty(engine.LoggedWarnings);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", prevEnv);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_ProcessArtifact_WithInvalidKey_FallsBackToDefault()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_SIGNING_KEY");
        var tempFile = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", "invalid_length_key");
            File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });

            var engine = new MockBuildEngine();
            var log = new TaskLoggingHelper(engine, "MockTask");

            Assert.True(ArtifactHelper.ProcessArtifact(tempFile, log));

            var resultData = File.ReadAllBytes(tempFile);
            Assert.Equal(3 + 64, resultData.Length);
            Assert.Contains(engine.LoggedWarnings, w => w.Contains("Failed to parse") || w.Contains("invalid"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_SIGNING_KEY", prevEnv);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
