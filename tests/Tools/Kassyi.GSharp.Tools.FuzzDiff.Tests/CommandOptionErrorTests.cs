#nullable enable

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Pins the error text and the accepted spellings of the options that <c>gen</c>, <c>reduce</c>, <c>dedup</c>,
/// <c>promote</c> and <c>compare</c> read through the shared option table, so that a change to the table shows up here.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class CommandOptionErrorTests
{
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--fuzz-seed", "--fuzz-seed requires a value.")]
    [InlineData("--input-seed", "--input-seed requires a value.")]
    [InlineData("--allow-issue", "--allow-issue requires a value.")]
    public void Gen_OptionWithoutValue_ReportsAMissingValue(string option, string expected)
    {
        bool parsed = GenCommand.TryParseOptions([option], out var options, out _, out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal(expected, error);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--fuzz-seed", "abc", "Invalid --fuzz-seed 'abc'.")]
    [InlineData("--input-seed", "x", "--input-seed requires an integer.")]
    [InlineData("--batch-size", "0", "--batch-size requires an integer in [1, 256].")]
    [InlineData("--size", "9", "--size requires an integer in [1, 8].")]
    public void Gen_InvalidValue_ReportsTheOption(string option, string value, string expected)
    {
        bool parsed = GenCommand.TryParseOptions([option, value], out var options, out _, out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal(expected, error);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Gen_UnknownOption_NamesTheArgument()
    {
        bool parsed = GenCommand.TryParseOptions(["--bogus"], out _, out _, out string? error);

        Assert.False(parsed);
        Assert.Equal("Unknown option '--bogus'.", error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Gen_EqualsSpelling_IsReadLikeTheSeparateValue()
    {
        bool joined = GenCommand.TryParseOptions(
            ["--fuzz-seed=0x10", "--batch-size=2"],
            out var joinedOptions,
            out _,
            out _);
        bool separate = GenCommand.TryParseOptions(
            ["--fuzz-seed", "0x10", "--batch-size", "2"],
            out var separateOptions,
            out _,
            out _);

        Assert.True(joined);
        Assert.True(separate);
        Assert.Equal(0x10UL, joinedOptions?.FuzzOptions.FuzzSeed);
        Assert.Equal(2, joinedOptions?.FuzzOptions.BatchSize);
        Assert.Equal(separateOptions?.FuzzOptions.FuzzSeed, joinedOptions?.FuzzOptions.FuzzSeed);
        Assert.Equal(separateOptions?.FuzzOptions.BatchSize, joinedOptions?.FuzzOptions.BatchSize);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Dedup_SourceWithoutValue_ReportsAMissingValue()
    {
        bool parsed = DedupCommand.TryParseOptions(["--source"], out var options, out _, out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("--source requires a value.", error);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Promote_InvalidFuzzSeed_ReportsTheOption()
    {
        bool parsed = PromoteCommand.TryParseOptions(
            ["--source", "a.cu.cs", "--fuzz-seed", "zz"],
            out var options,
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("Invalid --fuzz-seed 'zz'.", error);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--work-dir=")]
    [InlineData("--repo-root=")]
    public void Reduce_EmptyDirectory_ReportsADirectoryPath(string argument)
    {
        bool parsed = ReduceCommand.TryParseOptions(
            ["--source", "a.cu.cs", argument],
            out var options,
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal($"{argument[..^1]} requires a directory path.", error);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("--work-dir=")]
    [InlineData("--repo-root=")]
    public void Compare_EmptyDirectory_ReportsADirectoryPath(string argument)
    {
        bool parsed = CompareCommand.TryParseOptions(
            ["--base", "main", argument],
            out var options,
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal($"{argument[..^1]} requires a directory path.", error);
    }
}
