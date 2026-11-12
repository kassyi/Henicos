#nullable enable
using Kassyi.GSharp.Compiler.Tasks;
using System.Diagnostics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

/// <summary>
/// The <c>-gencode</c> options that <see cref="CompileCudaToFatbinTask"/> builds from <c>GSharpTargetArchitectures</c>,
/// checked on the nvcc command line that the task passes to the process runner.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileCudaToFatbinTaskArchitectureTests
{
    private const string InvalidArchitectureErrorCode = "GS0004";

    static CompileCudaToFatbinTaskArchitectureTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // nvcc -arch=sm_89 is short for SASS for sm_89 plus PTX for compute_89
    [InlineData("sm_89", "-gencode arch=compute_89,code=sm_89 -gencode arch=compute_89,code=compute_89 ")]
    [InlineData("compute_89,sm_89", "-gencode arch=compute_89,code=sm_89 ")]
    [InlineData("compute_89,compute_89", "-gencode arch=compute_89,code=compute_89 ")]
    [InlineData("compute_89", "-gencode arch=compute_89,code=compute_89 ")]
    // An architecture-specific suffix stays on the PTX too, as nvcc -arch=sm_90a does
    [InlineData("sm_90a", "-gencode arch=compute_90a,code=sm_90a -gencode arch=compute_90a,code=compute_90a ")]
    [InlineData("sm_100f", "-gencode arch=compute_100f,code=sm_100f -gencode arch=compute_100f,code=compute_100f ")]
    [InlineData(" compute_80 , sm_86 ", "-gencode arch=compute_80,code=sm_86 ")]
    [InlineData(
        "compute_75,sm_75;sm_89;;compute_90",
        "-gencode arch=compute_75,code=sm_75 " +
        "-gencode arch=compute_89,code=sm_89 -gencode arch=compute_89,code=compute_89 " +
        "-gencode arch=compute_90,code=compute_90 "
    )]
    public void TargetArchitectures_ValidEntries_BecomeGencodeOptions(string architectures, string expectedGencodes)
    {
        var arguments = RunNvcc(architectures);

        Assert.Equal(expectedGencodes, GetGencodes(arguments));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetArchitectures_Default_KeepsSm86SassAndCompute86Ptx()
    {
        var arguments = RunNvcc(CompileCudaToFatbinTask.DefaultTargetArchitectures);

        Assert.Equal(
            "-gencode arch=compute_86,code=sm_86 -gencode arch=compute_86,code=compute_86 ",
            GetGencodes(arguments)
        );
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("foo")]
    [InlineData("sm_89,compute_89")]
    [InlineData("compute_89,")]
    [InlineData(",sm_89")]
    [InlineData("compute_89,sm_89,compute_89")]
    [InlineData("sm89")]
    [InlineData("sm_8")]
    [InlineData("compute_89,sm_89x")]
    public void TargetArchitectures_MalformedEntry_FailsWithErrorNamingTheEntry(string entry)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu", "// cuda source");
        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null);
        task.TargetArchitectures = "compute_86,sm_86;" + entry;

        Assert.False(task.Execute());

        var code = Assert.Single(ctx.BuildEngine.LoggedErrorCodes);
        Assert.Equal(InvalidArchitectureErrorCode, code);
        var message = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Equal(
            $"GSharpTargetArchitectures entry '{entry.Trim()}' is not a GPU architecture nvcc accepts. " +
            "Write 'compute_XY,sm_XY' (SASS for X.Y), 'compute_XY,compute_XY' (PTX for X.Y), " +
            "'sm_XY' (SASS and PTX for X.Y, as nvcc -arch=sm_XY) or 'compute_XY' (PTX for X.Y); " +
            "an architecture may end in 'a' or 'f'.",
            message
        );
        Assert.DoesNotContain(ctx.Runner.Invocations, IsNvccFatbinRun);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TargetArchitectures_SeveralMalformedEntries_ReportsEachOne()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu", "// cuda source");
        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null);
        task.TargetArchitectures = "foo;compute_86,sm_86;bar";

        Assert.False(task.Execute());

        Assert.Equal([InvalidArchitectureErrorCode, InvalidArchitectureErrorCode], ctx.BuildEngine.LoggedErrorCodes);
        Assert.Contains("'foo'", ctx.BuildEngine.LoggedErrors[0]);
        Assert.Contains("'bar'", ctx.BuildEngine.LoggedErrors[1]);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(true, null)]
    [InlineData(false, "1")]
    public void TargetArchitectures_MalformedEntryWithoutNvcc_StillFails(bool mockCompilation, string? noCuda)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", noCuda);
        ctx.CreateDummySource("kernel.cu", "// cuda source");
        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null);
        task.MockCompilation = mockCompilation;
        task.TargetArchitectures = "sm89";

        Assert.False(task.Execute());

        Assert.Equal([InvalidArchitectureErrorCode], ctx.BuildEngine.LoggedErrorCodes);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("sm_89")]
    [InlineData("compute_86,sm_86;compute_86,compute_86;sm_90a;compute_100")]
    [InlineData("")]
    public void ValidateTask_ValidEntries_SucceedsAndLogsNothing(string architectures)
    {
        var engine = new MockBuildEngine();
        var task = new ValidateCudaArchitecturesTask { BuildEngine = engine, TargetArchitectures = architectures };

        Assert.True(task.Execute());

        Assert.Empty(engine.LoggedErrors);
        Assert.Empty(engine.LoggedWarnings);
        Assert.Empty(engine.LoggedMessages);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ValidateTask_MalformedEntries_LogsGs0004PerEntry()
    {
        var engine = new MockBuildEngine();
        var task = new ValidateCudaArchitecturesTask { BuildEngine = engine, TargetArchitectures = "sm89;sm_89;foo" };

        Assert.False(task.Execute());

        Assert.Equal([InvalidArchitectureErrorCode, InvalidArchitectureErrorCode], engine.LoggedErrorCodes);
        Assert.Contains("'sm89'", engine.LoggedErrors[0]);
        Assert.Contains("'foo'", engine.LoggedErrors[1]);
        Assert.Empty(engine.LoggedMessages);
    }

    private static string RunNvcc(string architectures)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu", "// cuda source");
        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null);
        task.TargetArchitectures = architectures;

        Assert.True(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);

        return Assert.Single(ctx.Runner.Invocations, IsNvccFatbinRun).Arguments;
    }

    // The -gencode options, in order, each followed by one space.
    private static string GetGencodes(string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var gencodes = "";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "-gencode")
            {
                gencodes += $"-gencode {parts[i + 1]} ";
            }
        }

        return gencodes;
    }

    private static bool IsNvccFatbinRun(ProcessStartInfo i) =>
        i.FileName.Contains("nvcc") && i.Arguments.Contains("--fatbin");
}
