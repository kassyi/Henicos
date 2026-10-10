#nullable enable
using Kassyi.GSharp.Core;
using Microsoft.Extensions.Logging;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;
using GSharpLogLevel = Kassyi.GSharp.Core.LogLevel;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class LoggerAndEnvironmentTests
{
    private class TestLoggerConsumer : Logger
    {
        protected override string GetTag() => "TestLoggerConsumer";

        public static void InvokePrintAccumulated(string tag, string prefix, GSharpLogLevel level = GSharpLogLevel.Debug) =>
            PrintAccumulatedInternal(tag, prefix, level);

        public static void InvokeAddInterval(string key, long ms) =>
            AddInterval(key, ms);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Logger_AllLevels_EmitWithoutException()
    {
        // Happy Path: Logger methods should succeed across all severity levels
        var prevLevel = Logger.ActiveLogLevel;
        var prevConsole = Logger.WriteToConsole;
        try
        {
            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            Logger.WriteToConsole = false; // Suppress output during tests

            var exception = Record.Exception(() =>
            {
                Logger.Trace("TEST_TAG", "Trace message");
                Logger.Verbose("TEST_TAG", "Verbose message");
                Logger.Debug("TEST_TAG", "Debug message");
                Logger.Info("TEST_TAG", "Info message");
                Logger.Warning("TEST_TAG", "Warning message");
                Logger.Error("TEST_TAG", "Error message");
                Logger.Error("TEST_TAG", new InvalidOperationException("Test ex"), "Error with exception");
            });

            Assert.Null(exception);
        }
        finally
        {
            Logger.ActiveLogLevel = prevLevel;
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Logger_PerformanceTrackingAndScopes_WorkCorrectly()
    {
        // Happy Path: Performance timer and measure scope
        TestLoggerConsumer.ClearAccumulated();
        TestLoggerConsumer.InvokeAddInterval("KernelA", 42);
        TestLoggerConsumer.InvokeAddInterval("KernelA", 18);
        TestLoggerConsumer.InvokePrintAccumulated("PERF", "Summary");
        TestLoggerConsumer.ClearAccumulated();

        var timer = new Logger.PerformanceTimer();
        Thread.Sleep(5);
        var elapsed = timer.Lap("TimedOp");
        Assert.True(elapsed >= 0);

        using (var scope = new Logger.MeasureScope("PERF", "ScopedOperation", GSharpLogLevel.Debug))
        {
            Thread.Sleep(5);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Logger_GenericILoggerWrapper_OperatesAsExpected()
    {
        // Happy Path: Microsoft.Extensions.Logging.ILogger implementation via Logger<T>
        var prevLevel = Logger.ActiveLogLevel;
        try
        {
            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            ILogger logger = new Kassyi.GSharp.Core.Logger<LoggerAndEnvironmentTests>();
            Assert.NotNull(logger);

            Assert.Null(logger.BeginScope("ScopeState"));
            Assert.True(logger.IsEnabled(MSLogLevel.Information));

            logger.Log(MSLogLevel.Information, new EventId(1), "Test state", null, (s, _) => s);
            logger.Log(MSLogLevel.Error, new EventId(2), "Error state", new Exception("Fail"), (s, ex) => $"{s}: {ex?.Message}");
        }
        finally
        {
            Logger.ActiveLogLevel = prevLevel;
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Special\t\r\n\0Characters")]
    public void Test_Logger_BoundaryMessages_HandledGracefully(string message)
    {
        // Boundary Testing: Empty string, whitespace, and special characters
        var prevConsole = Logger.WriteToConsole;
        try
        {
            Logger.WriteToConsole = false;
            var exception = Record.Exception(() => Logger.Info("BOUNDARY", message));
            Assert.Null(exception);
        }
        finally
        {
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Logger_OversizedMessage_HandledGracefully()
    {
        // Boundary Testing: Very large message (100,000 characters)
        var prevConsole = Logger.WriteToConsole;
        try
        {
            Logger.WriteToConsole = false;
            var hugeMessage = new string('A', 100_000);
            var exception = Record.Exception(() => Logger.Info("OVERSIZED", hugeMessage));
            Assert.Null(exception);
        }
        finally
        {
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Logger_NullMessage_IgnoredWithoutException()
    {
        // Sad Path: Null message should be ignored cleanly without throwing NullReferenceException
        var exception = Record.Exception(() => Logger.Log(GSharpLogLevel.Info, "TAG", null));
        Assert.Null(exception);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Logger_FilteringBelowActiveLevel_SuppressesLogs()
    {
        // Sad Path: Messages below active level must be suppressed
        var prevLevel = Logger.ActiveLogLevel;
        try
        {
            Logger.ActiveLogLevel = GSharpLogLevel.Error;
            ILogger logger = new Kassyi.GSharp.Core.Logger<LoggerAndEnvironmentTests>();

            Assert.False(logger.IsEnabled(MSLogLevel.Debug));
            Assert.False(logger.IsEnabled(MSLogLevel.Information));
            Assert.False(logger.IsEnabled(MSLogLevel.Warning));
            Assert.True(logger.IsEnabled(MSLogLevel.Error));
            Assert.True(logger.IsEnabled(MSLogLevel.Critical));
        }
        finally
        {
            Logger.ActiveLogLevel = prevLevel;
        }
    }

    [Fact]
    [Category(TestCategory.Stress)]
    public void Test_Logger_ConcurrentWrites_ThreadSafeAndNoDeadlock()
    {
        // Stress & Concurrency: Multi-threaded burst logging into the background queue
        var prevConsole = Logger.WriteToConsole;
        try
        {
            Logger.WriteToConsole = false;
            Parallel.For(0, 500, i =>
            {
                Logger.Info("CONCURRENCY", $"Log entry {i} from thread {Environment.CurrentManagedThreadId}");
                TestLoggerConsumer.InvokeAddInterval($"Task_{i % 10}", i);
            });
        }
        finally
        {
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaEnvironment_PathResolution_DoesNotCrash()
    {
        // Happy Path: CudaEnvironment API calls return either valid paths or null without throwing unhandled exceptions
        var cudaPath = CudaEnvironment.GetCudaPath();
        var binPath = CudaEnvironment.GetCudaBinPath();

        if (cudaPath != null)
        {
            Assert.True(Directory.Exists(cudaPath));
        }

        if (binPath != null)
        {
            Assert.True(Directory.Exists(binPath));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CudaEnvironment_InvalidCudaPath_Ignored()
    {
        // Sad Path: Setting an invalid CUDA_PATH environment variable should be ignored and fallback safely
        var originalEnv = Environment.GetEnvironmentVariable("CUDA_PATH");
        try
        {
            string invalidPath = OperatingSystem.IsWindows() ? "X:\\Invalid\\Path\\To\\Cuda" : "/invalid/path/to/cuda";
            Environment.SetEnvironmentVariable("CUDA_PATH", invalidPath);

            var path = CudaEnvironment.GetCudaPath();
            var binPath = CudaEnvironment.GetCudaBinPath();

            // It should not return the invalid path. It may return a valid fallback or null.
            Assert.NotEqual(invalidPath, path);
            if (path != null)
            {
                Assert.True(Directory.Exists(path));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CUDA_PATH", originalEnv);
        }
    }

    private class MockCudaPathResolver : DefaultCudaPathResolver
    {
        public Dictionary<string, string?> EnvVars { get; } = new();
        public HashSet<string> ExistingDirs { get; } = new();
        public Dictionary<string, string[]> SubDirs { get; } = new();
        public bool MockIsWindows { get; set; } = false;
        public bool MockIsLinux { get; set; } = false;

        protected override string? GetEnvironmentVariable(string name) => EnvVars.TryGetValue(name, out var v) ? v : null;
        protected override bool DirectoryExists(string path) => ExistingDirs.Contains(path);
        protected override string[] GetDirectories(string path, string searchPattern) => SubDirs.TryGetValue(path, out var dirs) ? dirs : [];
        protected override bool IsWindows() => MockIsWindows;
        protected override bool IsLinux() => MockIsLinux;
    }

    [Fact]
    public void Test_CudaEnvironment_Windows_FallbackPaths()
    {
        var mock = new MockCudaPathResolver { MockIsWindows = true };
        var baseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        mock.ExistingDirs.Add(baseDir);
        mock.SubDirs[baseDir] = [
            Path.Combine(baseDir, "v11.8"),
            Path.Combine(baseDir, "v12.0"),
            Path.Combine(baseDir, "v10.0")
        ];

        CudaEnvironment.Resolver = mock;
        try
        {
            var path = CudaEnvironment.GetCudaPath();
            Assert.Equal(Path.Combine(baseDir, "v12.0"), path);
        }
        finally
        {
            CudaEnvironment.Resolver = new DefaultCudaPathResolver();
        }
    }

    [Fact]
    public void Test_CudaEnvironment_Linux_FallbackPath()
    {
        var mock = new MockCudaPathResolver { MockIsLinux = true };
        mock.ExistingDirs.Add("/usr/local/cuda");

        CudaEnvironment.Resolver = mock;
        try
        {
            var path = CudaEnvironment.GetCudaPath();
            Assert.Equal("/usr/local/cuda", path);

            // Test Bin path logic
            var expectedBin = Path.Combine("/usr/local/cuda", "bin");
            mock.ExistingDirs.Add(expectedBin);
            var binPath = CudaEnvironment.GetCudaBinPath();
            Assert.Equal(expectedBin, binPath);
        }
        finally
        {
            CudaEnvironment.Resolver = new DefaultCudaPathResolver();
        }
    }

    [Fact]
    public void Test_CudaEnvironment_Windows_BinPath()
    {
        var mock = new MockCudaPathResolver { MockIsWindows = true };
        mock.EnvVars["CUDA_PATH"] = @"C:\Cuda";
        mock.ExistingDirs.Add(@"C:\Cuda");
        mock.ExistingDirs.Add(@"C:\Cuda\bin\x64");

        CudaEnvironment.Resolver = mock;
        try
        {
            var binPath = CudaEnvironment.GetCudaBinPath();
            Assert.Equal(@"C:\Cuda\bin\x64", binPath);
        }
        finally
        {
            CudaEnvironment.Resolver = new DefaultCudaPathResolver();
        }
    }
}
