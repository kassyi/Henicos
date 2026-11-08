namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>
/// The command line of the generator tool: the MSBuild targets see only the exit code, so a wrong argument list must
/// fail instead of running another mode or printing the usage with success.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class ProgramArgumentTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NoArguments_FailsWithUsage()
    {
        var result = GeneratorRun.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No mode given", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Usage:", result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("native-api")]
    [InlineData("diagnostics")]
    [InlineData("prelude")]
    [InlineData("internal-names")]
    public void ModeWithoutPaths_FailsWithUsage(string mode)
    {
        var result = GeneratorRun.Run(mode);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains($"Wrong number of arguments for the mode '{mode}'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DialectsWithoutOutput_FailsAndCreatesNothing()
    {
        using var scratch = new ScratchDirectory();
        var input = scratch.Combine("in");

        var result = GeneratorRun.Run("dialects", input);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Wrong number of arguments for the mode 'dialects'", result.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(input));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MisspelledMode_FailsAndCreatesNothing()
    {
        using var scratch = new ScratchDirectory();
        var input = scratch.Combine("in");
        var output = scratch.Combine("out");

        var result = GeneratorRun.Run("native-apis", input, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unknown mode 'native-apis'", result.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(input));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExtraArgument_Fails()
    {
        using var scratch = new ScratchDirectory();
        var output = scratch.Combine("out");

        var result = GeneratorRun.Run("dialects", scratch.Path, output, "extra");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Wrong number of arguments for the mode 'dialects' (3 given)", result.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MissingInputDirectory_FailsNamingIt()
    {
        using var scratch = new ScratchDirectory();
        var input = scratch.Combine("absent");

        var result = GeneratorRun.Run("dialects", input, scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{input}: error : The dialect YAML directory does not exist.", result.StandardError, StringComparison.Ordinal);
    }
}
