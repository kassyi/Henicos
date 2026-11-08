using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>
/// The <c>prelude</c> mode inlines the relative <c>#include "..."</c> directives of <c>Prelude.cu</c> and
/// <c>Prelude.cpp</c>. An include that does not resolve, or that comes back around to a file being included, fails the
/// run with the including file and line; it is never left in the prelude or expanded without end.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class PreludeGeneratorTests
{
    // A cycle used to expand without end; a run that does not finish in this time fails the test instead of hanging.
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NestedIncludes_AreInlined()
    {
        using var scratch = CreateResources(cuda: "#include \"a.cuh\"\nint cu;\n", cpp: "#include \"Prelude/a.hpp\"\n");
        scratch.Write("res/a.cuh", "#include \"b.cuh\"\nint a;\n");
        scratch.Write("res/b.cuh", "int b;\n");
        scratch.Write("res/Prelude/a.hpp", "int a;\n#include \"Prelude/b.hpp\"\n");
        scratch.Write("res/Prelude/b.hpp", "int b;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(0, result.ExitCode);
        var (cuda, cpp) = ReadPreludes(scratch);
        Assert.Equal("int b;\n\nint a;\n\nint cu;\n\n", cuda);
        // The C++ prelude drops one trailing line break of a header included at its very end.
        Assert.Equal("int a;\nint b;\n\n", cpp);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HeaderAtTheEndOfAHeaderNotAtTheEnd_KeepsItsLineBreak()
    {
        using var scratch = CreateResources(cuda: "int cu;\n", cpp: "#include \"Prelude/a.hpp\"\nint tail;\n");
        scratch.Write("res/Prelude/a.hpp", "int a;\n#include \"Prelude/b.hpp\"\n");
        scratch.Write("res/Prelude/b.hpp", "int b;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("int a;\nint b;\n\n\nint tail;\n\n", ReadPreludes(scratch).Cpp);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HeaderIncludedTwiceWithoutCycle_IsInlinedTwice()
    {
        using var scratch = CreateResources(cuda: "#include \"a.cuh\"\n#include \"a.cuh\"\n", cpp: "int cpp;\n");
        scratch.Write("res/a.cuh", "int a;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("int a;\n\nint a;\n\n\n", ReadPreludes(scratch).Cuda);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MissingInclude_FailsWithIncludingFileAndLine()
    {
        using var scratch = CreateResources(cuda: "int a;\n#include \"missing.cuh\"\n", cpp: "int cpp;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{scratch.Combine("res", "Prelude.cu")}(2): error : Cannot find the included file 'missing.cuh'", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(scratch.Combine("out", "GSharpPreludeResources.g.cs")));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MissingNestedInclude_NamesTheHeaderThatIncludesIt()
    {
        using var scratch = CreateResources(cuda: "int cu;\n", cpp: "#include \"Prelude/a.hpp\"\n");
        var header = scratch.Write("res/Prelude/a.hpp", "int a;\n\n#include \"Prelude/gone.hpp\"\n");

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{header}(3): error : Cannot find the included file 'Prelude/gone.hpp'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IncludeCycle_FailsWithTheChain()
    {
        using var scratch = CreateResources(cuda: "#include \"a.cuh\"\n", cpp: "int cpp;\n");
        scratch.Write("res/a.cuh", "#include \"b.cuh\"\n");
        var b = scratch.Write("res/b.cuh", "int b;\n#include \"a.cuh\"\n");

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{b}(2): error : Include cycle: Prelude.cu -> a.cuh -> b.cuh -> a.cuh.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FileThatIncludesItself_IsACycle()
    {
        using var scratch = CreateResources(cuda: "#include \"Prelude.cu\"\n", cpp: "int cpp;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error : Include cycle: Prelude.cu -> Prelude.cu.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IncludeWithoutClosingQuote_Fails()
    {
        using var scratch = CreateResources(cuda: "#include \"a.cuh\nint \"x\";\n", cpp: "int cpp;\n");
        scratch.Write("res/a.cuh", "int a;\n");

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Prelude.cu(1): error : The #include directive has no closing quote on its line.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MissingRootPrelude_FailsNamingIt()
    {
        using var scratch = CreateResources(cuda: "int cu;\n", cpp: "int cpp;\n");
        File.Delete(scratch.Combine("res", "Prelude.cpp"));

        var result = RunPrelude(scratch);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{scratch.Combine("res", "Prelude.cpp")}: error : The prelude file does not exist.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AngleBracketIncludes_AreLeftForTheCompiler()
    {
        using var scratch = CreateResources(cuda: "#include <mma.h>\nint cu;\n", cpp: "#include <cstdint>\n");

        var result = RunPrelude(scratch);

        Assert.Equal(0, result.ExitCode);
        var (cuda, cpp) = ReadPreludes(scratch);
        Assert.Equal("#include <mma.h>\nint cu;\n\n", cuda);
        Assert.Equal("#include <cstdint>\n\n", cpp);
    }

    private static ScratchDirectory CreateResources(string cuda, string cpp)
    {
        var scratch = new ScratchDirectory();
        scratch.Write("res/Prelude.cu", cuda);
        scratch.Write("res/Prelude.cpp", cpp);
        scratch.Write("res/InternalNames.yml", "");
        return scratch;
    }

    private static GeneratorResult RunPrelude(ScratchDirectory scratch)
    {
        var run = Task.Run(() => GeneratorRun.Run("prelude", scratch.Combine("res"), scratch.Combine("out")));
        Assert.True(run.Wait(s_timeout), $"The prelude mode did not finish within {s_timeout}.");
        return run.Result;
    }

    private static (string Cuda, string Cpp) ReadPreludes(ScratchDirectory scratch)
    {
        var root = GeneratorRun.ParseWithoutErrors(File.ReadAllText(scratch.Combine("out", "GSharpPreludeResources.g.cs")));
        var constants = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .ToDictionary(
                static v => v.Identifier.ValueText,
                static v => v.Initializer?.Value is LiteralExpressionSyntax literal ? literal.Token.ValueText : ""
            );
        return (constants["Cuda"], constants["Cpp"]);
    }
}
