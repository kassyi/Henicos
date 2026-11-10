#nullable enable
using System.Text.Json;

namespace Kassyi.GSharp.Tools.CollectEnv.Tests;

/// <summary>
/// Runs tools/collect-env/collect-env.cs with a fake <c>nvidia-smi</c> and <c>nvcc</c> first on <c>PATH</c>, so that
/// the parsing of their text is pinned without a GPU. The fakes are shell scripts, so these tests run on Unix only.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CollectEnvGpuProbeTests
{
    private const string QueryVariable = "FAKE_SMI_QUERY";
    private const string HeaderVariable = "FAKE_SMI_HEADER";
    private const string NvccVariable = "FAKE_NVCC";

    private const string SmiScript =
        """
        #!/bin/sh
        case "$1" in
          --query-gpu*) printf '%s\n' "$FAKE_SMI_QUERY" ;;
          *) printf '%s\n' "$FAKE_SMI_HEADER" ;;
        esac
        """;

    private const string NvccScript =
        """
        #!/bin/sh
        printf '%s\n' "$FAKE_NVCC"
        """;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Gpus_NewerDriverHeader_ReadsTheUmdVersion()
    {
        JsonElement report = Collect(
            query: "NVIDIA GeForce RTX 3070 Ti, 8.6, 8192 MiB, 610.88",
            header: "| NVIDIA-SMI 610.88  Driver Version: 610.88  CUDA UMD Version: 13.3 |");

        Assert.Equal(
            "NVIDIA GeForce RTX 3070 Ti, sm_86, 8 GB, driver 610.88 (driver CUDA 13.3)",
            Assert.Single(report.GetProperty("gpus").EnumerateArray()).GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Gpus_OlderDriverHeader_ReadsTheCudaVersion()
    {
        JsonElement report = Collect(
            query: "Tesla T4, 7.5, 15360 MiB, 535.54",
            header: "| NVIDIA-SMI 535.54  Driver Version: 535.54  CUDA Version: 12.2 |");

        Assert.Equal(
            "Tesla T4, sm_75, 15 GB, driver 535.54 (driver CUDA 12.2)",
            Assert.Single(report.GetProperty("gpus").EnumerateArray()).GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Gpus_HeaderWithoutCudaVersion_OmitsTheDriverCuda()
    {
        JsonElement report = Collect(query: "Tesla T4, 7.5, 15360 MiB, 535.54", header: "NVIDIA-SMI has failed");

        Assert.Equal(
            "Tesla T4, sm_75, 15 GB, driver 535.54",
            Assert.Single(report.GetProperty("gpus").EnumerateArray()).GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Gpus_MemoryWithoutMiBSuffix_IsKeptAsPrinted()
    {
        JsonElement report = Collect(query: "Tesla T4, 7.5, 16 GB, 535.54", header: string.Empty);

        Assert.Equal(
            "Tesla T4, sm_75, 16 GB, driver 535.54",
            Assert.Single(report.GetProperty("gpus").EnumerateArray()).GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Gpus_LineWithFewerThanFourFields_IsSkipped()
    {
        JsonElement report = Collect(
            query: "Tesla T4, 7.5, 15360 MiB\nTesla V100, 7.0, 16384 MiB, 535.54",
            header: string.Empty);

        Assert.Equal(
            "Tesla V100, sm_70, 16 GB, driver 535.54",
            Assert.Single(report.GetProperty("gpus").EnumerateArray()).GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaToolkit_ReleaseLine_ShowsReleaseAndBuild()
    {
        JsonElement report = Collect(
            nvcc: "nvcc: NVIDIA (R) Cuda compiler driver\nCuda compilation tools, release 12.4, V12.4.131");

        Assert.Equal("nvcc 12.4 (V12.4.131)", report.GetProperty("cudaToolkit").GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CudaToolkit_ReleaseWithoutBuild_ShowsTheRelease()
    {
        JsonElement report = Collect(nvcc: "Cuda compilation tools, release 12.4");

        Assert.Equal("nvcc 12.4", report.GetProperty("cudaToolkit").GetString());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CudaToolkit_OutputWithoutRelease_ShowsTheFirstLine()
    {
        JsonElement report = Collect(nvcc: "nvcc: something unexpected\nsecond line");

        Assert.Equal("nvcc: something unexpected", report.GetProperty("cudaToolkit").GetString());
    }

    private static JsonElement Collect(string query = "", string header = "", string nvcc = "")
    {
        Skip.If(OperatingSystem.IsWindows(), "The fake nvidia-smi and nvcc are shell scripts.");

        string directory = Path.Combine(Path.GetTempPath(), "collect-env-fakes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteExecutable(Path.Combine(directory, "nvidia-smi"), SmiScript);
            WriteExecutable(Path.Combine(directory, "nvcc"), NvccScript);
            (int exitCode, string stdout, string stderr) = CollectEnvToolRunner.Run(
                ["--json"],
                environment =>
                {
                    environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                    environment[QueryVariable] = query;
                    environment[HeaderVariable] = header;
                    environment[NvccVariable] = nvcc;
                });

            Assert.True(exitCode == 0, stderr);
            using JsonDocument document = JsonDocument.Parse(stdout);
            return document.RootElement.Clone();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content + "\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
