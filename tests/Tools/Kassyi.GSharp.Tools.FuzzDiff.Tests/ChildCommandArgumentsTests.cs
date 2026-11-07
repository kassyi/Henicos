#nullable enable

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests that the command lines <c>fuzz-diff run</c> builds for its <c>reduce</c> and <c>promote</c> child processes
/// are read back by the parsers of those commands into the same options.
/// </summary>
public sealed class ChildCommandArgumentsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fuzz-args-{Guid.NewGuid():N}");
    private readonly string _source;

    public ChildCommandArgumentsTests()
    {
        Directory.CreateDirectory(_directory);
        _source = Path.Combine(_directory, "kernel.cu.cs");
        File.WriteAllText(_source, "// kernel");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ReduceToArguments_EveryOptionSet_RoundTrips()
    {
        var options = new ReduceCommandOptions(
            SourcePath: _source,
            FuzzSeed: 0xFEDCBA9876543210UL,
            KernelName: "Kernel_07",
            InputSeed: -3,
            Profile: "G",
            WorkDir: Path.Combine(_directory, "_reduce"),
            RepoRoot: _directory,
            TestCmd: "node check.js {source}",
            OutPath: Path.Combine(_directory, "reduced.cu.cs"),
            FailingPaths: "Managed,Cuda",
            FailureKind: "compile_error",
            Column: "out1",
            NoGpuLock: true);

        bool parsed = ReduceCommand.TryParseOptions(
            [.. ReduceCommand.ToArguments(options)],
            out var reparsed,
            out _,
            out string? error);

        // mutant: a flag name or a value format of ToArguments differs from what TryParseOptions reads
        Assert.True(parsed, error);
        Assert.Equal(options, reparsed);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ReduceToArguments_OptionalValuesUnset_RoundTripsWithTheirDefaults()
    {
        var options = new ReduceCommandOptions(
            SourcePath: null,
            FuzzSeed: 1UL,
            KernelName: null,
            InputSeed: 0,
            Profile: "C",
            WorkDir: "w",
            RepoRoot: null,
            TestCmd: null,
            OutPath: null,
            FailingPaths: "Native,Cuda",
            FailureKind: "CellMismatch",
            Column: "hash",
            NoGpuLock: false);

        bool parsed = ReduceCommand.TryParseOptions(
            [.. ReduceCommand.ToArguments(options)],
            out var reparsed,
            out _,
            out string? error);

        // mutant: a null value is written as an empty argument, or --no-gpu-lock is always written
        Assert.True(parsed, error);
        Assert.Equal(options, reparsed);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Kernel_u11")]
    [InlineData(null)]
    public void PromoteToArguments_Options_RoundTrip(string? kernel)
    {
        var options = new PromoteCommandOptions(
            SourcePath: _source,
            FuzzSeed: 0x2AUL,
            InputSeed: 8,
            FailingPaths: "Native",
            FailureKind: "CellMismatch",
            Column: "hash",
            FindingsDir: Path.Combine(_directory, "findings"),
            KernelName: kernel);

        bool parsed = PromoteCommand.TryParseOptions(
            [.. PromoteCommand.ToArguments(options)],
            out var reparsed,
            out _,
            out string? error);

        // mutant: --kernel is written without a value when no kernel is named, or a flag name differs
        Assert.True(parsed, error);
        Assert.Equal(options, reparsed);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
