#nullable enable
using System.Linq;
using System.Xml.Linq;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// The targets of the compiler project that run the build-time generator tool before CoreCompile. MSBuild skips such a
/// target when its outputs are newer than its inputs, so a change of the generator itself must count as an input, or
/// the sources of the previous generator stay in <c>obj/</c> (for example after a branch switch).
/// </summary>
[Category(TestCategory.Compiler)]
public class CompilerGeneratorTargetsTests
{
    private const string GeneratorDll = "Kassyi.GSharp.Compiler.Generators.dll";

    public static TheoryData<string> GeneratorTargets()
    {
        var data = new TheoryData<string>();
        foreach (var name in GeneratorTargetNames())
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GeneratorTargets_CompilerProject_AreFound()
    {
        Assert.Contains("GenerateYamlDialects", GeneratorTargetNames());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GeneratorTargets))]
    public void Inputs_TargetRunningTheGenerator_IncludeTheGeneratorAssembly(string targetName)
    {
        var target = LoadTargets().Single(t => Name(t) == targetName);

        var inputs = (string?)target.Attribute("Inputs") ?? string.Empty;

        Assert.Contains(GeneratorDll, inputs, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GenerateYamlDialects_BeforeTheGeneratorRuns_RemovesTheFilesOfTheLastRun()
    {
        // A generator that changed the names of its files would otherwise leave the old files to be compiled.
        var steps = LoadTargets().Single(static t => Name(t) == "GenerateYamlDialects").Elements().ToList();

        var removal = steps.FindIndex(static s => s.Name.LocalName == "RemoveDir"
            && ((string?)s.Attribute("Directories") ?? string.Empty)
                .Contains("GeneratedDialects", StringComparison.Ordinal));
        var generation = steps.FindIndex(static s => s.Name.LocalName == "Exec");

        Assert.InRange(removal, 0, generation - 1);
    }

    private static List<string> GeneratorTargetNames() => LoadTargets().Where(RunsTheGenerator).Select(Name).ToList();

    private static IEnumerable<XElement> LoadTargets()
    {
        var project = XDocument.Load(Path.Combine(RepositoryPaths.CompilerProject, "Kassyi.GSharp.Compiler.csproj"));
        return project.Root?.Elements("Target") ?? [];
    }

    private static bool RunsTheGenerator(XElement target) =>
        target.Elements("Exec").Any(static exec =>
            ((string?)exec.Attribute("Command") ?? string.Empty).Contains(GeneratorDll, StringComparison.Ordinal));

    private static string Name(XElement target) => (string?)target.Attribute("Name") ?? string.Empty;
}
