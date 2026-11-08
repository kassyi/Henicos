using System.Linq;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>
/// The <c>dialects</c> mode: a dialect YAML file that cannot be read fails the run with its file and line, the generated
/// classes hold the YAML text exactly, and a class no dialect wrote this time is not left for the build to compile.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class DialectGeneratorTests
{
    // Lines: 1 Dialect, 2 Backend, 3 Capabilities, 4 Types, 5 System.Int32, 6 Layout, 7 Pointer, 8 Intrinsics, 9 Abs.
    private static readonly string s_validDialect =
        """
        Dialect: Probe
        Backend: Cpp
        Capabilities: HardwareAtomics, SharedMemory
        Types:
          System.Int32: { cpp: "int", size: 4, align: 4 }
        Layout:
          Pointer: { size: 8, align: 8 }
        Intrinsics:
          System.Math.Abs(int): "abs"
        """.ReplaceLineEndings("\n");

    private static readonly string s_dialectSources = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Dialects");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RepositoryDialects_GenerateParsableClasses()
    {
        using var scratch = new ScratchDirectory();
        var output = scratch.Combine("out");

        var result = GeneratorRun.Run("dialects", s_dialectSources, output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardError);
        var files = Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["GSharpCudaDialect.g.cs", "StandardCppDialect.g.cs"], files);
        foreach (var file in Directory.GetFiles(output))
        {
            GeneratorRun.ParseWithoutErrors(File.ReadAllText(file));
        }
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("  System.Int32: { cpp: \"int, size: 4, align: 4 }", 5, "Unterminated")]
    [InlineData("  System.Int32: [int]", 5, "The type 'System.Int32' must map to a C++ type name")]
    [InlineData("  System.Int32: { cpp: \"int\", size: 4 }", 5, "must give both 'size' and 'align'")]
    [InlineData("  System.Int32: { cpp: \"int\", size: four, align: 4 }", 5, "'size' must be a positive integer")]
    public void BrokenTypeEntry_FailsWithFileAndLine(string typeLine, int line, string message)
    {
        using var scratch = new ScratchDirectory();
        var yaml = s_validDialect.Replace("  System.Int32: { cpp: \"int\", size: 4, align: 4 }", typeLine, StringComparison.Ordinal);
        var path = scratch.Write("in/Probe.yml", yaml);

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{path}({line}): error : ", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(message, result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Dialect: Probe", "Dialekt: Probe", 1, "Unknown key 'Dialekt'")]
    [InlineData("Dialect: Probe", "Dialect:", 1, "'Dialect' must be a single value")]
    [InlineData("Dialect: Probe", "Dialect: Pro-be", 1, "'Dialect: Pro-be' is not a valid C# identifier")]
    [InlineData("Intrinsics:\n  System.Math.Abs(int): \"abs\"", "Intrinsics: abs", 8, "'Intrinsics' must be a mapping")]
    [InlineData("  System.Math.Abs(int): \"abs\"", "  System.Math.Abs(int):", 9, "The intrinsic 'System.Math.Abs(int)' must map to a C++ expression")]
    [InlineData("  Pointer: { size: 8, align: 8 }", "  Pointer: { size: 8 }", 7, "The layout 'Pointer' must be")]
    [InlineData("Backend: Cpp", "Backend: Gpu", 1, "must set Backend to Cuda or Cpp")]
    [InlineData("Backend: Cpp\n", "", 1, "must set Backend to Cuda or Cpp")]
    [InlineData("Backend: Cpp", "Backend: Cpp\nInherits: Missing", 1, "inherits 'Missing', which no dialect YAML file defines")]
    [InlineData("Backend: Cpp", "Backend: Cpp\nInherits: Probe", 1, "inherits from itself")]
    [InlineData("HardwareAtomics, SharedMemory", "HardwareAtomics, Shared Memory", 1, "The capability 'Shared Memory'")]
    public void BrokenDialect_FailsWithFileAndLine(string original, string replacement, int line, string message)
    {
        using var scratch = new ScratchDirectory();
        var yaml = s_validDialect;
        Assert.Contains(original, yaml, StringComparison.Ordinal);
        var path = scratch.Write("in/Probe.yml", yaml.Replace(original, replacement, StringComparison.Ordinal));

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{path}({line}): error : ", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(message, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void BrokenDialect_LeavesNoOutputOfThisRun()
    {
        using var scratch = new ScratchDirectory();
        scratch.Write("in/Probe.yml", s_validDialect.Replace("Dialect:", "Dialekt:", StringComparison.Ordinal));
        var stale = scratch.Write("out/ProbeDialect.g.cs", "// stale content from an earlier build");

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        // The stale class stays on disk, but the run fails, so MSBuild stops before CoreCompile reads it.
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("// stale content from an earlier build", File.ReadAllText(stale));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DialectDefinedTwice_FailsNamingBothFiles()
    {
        using var scratch = new ScratchDirectory();
        var first = scratch.Write("in/A.yml", s_validDialect);
        var second = scratch.Write("in/B.yml", s_validDialect);

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{second}(1): error : The dialect 'Probe' is also defined in {first} ('Probe').", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DialectNamesDifferingOnlyInCase_Fail()
    {
        using var scratch = new ScratchDirectory();
        scratch.Write("in/A.yml", s_validDialect);
        var second = scratch.Write("in/B.yml", s_validDialect.Replace("Dialect: Probe", "Dialect: PROBE", StringComparison.Ordinal));

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{second}(1): error : The dialect 'PROBE' is also defined in", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ClassOfRemovedDialect_IsDeleted()
    {
        using var scratch = new ScratchDirectory();
        scratch.Write("in/Probe.yml", s_validDialect);
        var removed = scratch.Write("out/RemovedDialect.g.cs", "// a dialect that no longer has a YAML file");
        var unrelated = scratch.Write("out/notes.txt", "kept");

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(removed));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(scratch.Combine("out", "ProbeDialect.g.cs")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void QuotesBackslashesAndLineBreaks_AreKeptExactly()
    {
        using var scratch = new ScratchDirectory();
        // In a double-quoted YAML scalar, \" and \\ and \n are escapes: the values hold ", \ and a line break.
        var yaml = s_validDialect + "\n  Probe.Print(int): 'printf(\"%d\\n\", x)'\n  Probe.Path(int): \"C:\\\\temp\\\\x\"\n  Probe.Lines(int): \"a\\nb\\r\\nc\\td\"\n";
        scratch.Write("in/Probe.yml", yaml);

        var result = GeneratorRun.Run("dialects", scratch.Combine("in"), scratch.Combine("out"));

        Assert.Equal(0, result.ExitCode);
        var root = GeneratorRun.ParseWithoutErrors(File.ReadAllText(scratch.Combine("out", "ProbeDialect.g.cs")));
        var literals = GeneratorRun.StringLiteralValues(root);
        Assert.Contains("printf(\"%d\\n\", x)", literals);
        Assert.Contains("C:\\temp\\x", literals);
        Assert.Contains("a\nb\r\nc\td", literals);
        Assert.Contains("ProbeDialect", literals);
    }
}
