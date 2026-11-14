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
        var target = Target(targetName);

        var inputs = (string?)target.Attribute("Inputs") ?? string.Empty;

        Assert.Contains(GeneratorDll, inputs, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GeneratorTargets))]
    public void Steps_TargetRunningTheGenerator_GenerateBesideTheOutputAndSwapItIn(string targetName)
    {
        // The generator writes a fresh directory next to the output, which then replaces the output: files that the
        // generator no longer writes are not compiled, and no reader sees an empty output while the generator runs.
        var steps = Target(targetName).Elements().ToList();
        var generation = steps.FindIndex(static s => s.Name.LocalName == "Exec");
        var fresh = OutputArgument(steps[generation]);
        var swap = steps.FindIndex(static s => s.Name.LocalName == "Move");
        Assert.InRange(swap, generation + 1, steps.Count - 1);
        var output = (string?)steps[swap].Attribute("DestinationFolder") ?? string.Empty;

        Assert.NotEqual(fresh, output);
        Assert.InRange(IndexOfRemoval(steps, fresh, 0), 0, generation - 1);
        Assert.InRange(IndexOfRemoval(steps, output, generation), generation + 1, swap - 1);
        Assert.InRange(IndexOfRemoval(steps, fresh, swap), swap + 1, steps.Count - 1);
        Assert.Contains(
            steps.GetRange(generation + 1, swap - generation - 1),
            s => s.Name.LocalName == "ItemGroup" && s.Elements().Any(i => IncludesUnder(i, fresh))
        );
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GeneratorTargets))]
    public void Includes_GeneratedSources_DependOnTheirGeneratorTarget(string targetName)
    {
        // A target that compiles the generated sources must run after the generator, or it compiles the old ones.
        var swap = Target(targetName).Elements("Move").Single();
        var output = (string?)swap.Attribute("DestinationFolder") ?? string.Empty;

        var includers = LoadTargets()
            .Where(t => t.Elements("ItemGroup").Elements("Compile").Any(i => IncludesUnder(i, output)))
            .ToList();

        Assert.NotEmpty(includers);
        Assert.All(includers, t => Assert.True(
            ListsTarget((string?)t.Attribute("DependsOnTargets") ?? string.Empty, targetName),
            $"{Name(t)} compiles the output of {targetName} but does not depend on it."
        ));
    }

    private static List<string> GeneratorTargetNames() => LoadTargets().Where(RunsTheGenerator).Select(Name).ToList();

    private static XElement Target(string name) => LoadTargets().Single(t => Name(t) == name);

    private static IEnumerable<XElement> LoadTargets()
    {
        var project = XDocument.Load(Path.Combine(RepositoryPaths.CompilerProject, "Kassyi.GSharp.Compiler.csproj"));
        return project.Root?.Elements("Target") ?? [];
    }

    private static bool RunsTheGenerator(XElement target) =>
        target.Elements("Exec").Any(static exec =>
            ((string?)exec.Attribute("Command") ?? string.Empty).Contains(GeneratorDll, StringComparison.Ordinal));

    // The directory the generator writes: the last quoted argument of its command line.
    private static string OutputArgument(XElement exec)
    {
        var command = (string?)exec.Attribute("Command") ?? string.Empty;
        var end = command.LastIndexOf('"');
        var start = end > 0 ? command.LastIndexOf('"', end - 1) : -1;
        Assert.True(start >= 0, $"No quoted output argument in: {command}");
        return command.AsSpan(start + 1, end - start - 1).ToString();
    }

    private static int IndexOfRemoval(List<XElement> steps, string directory, int from) =>
        steps.FindIndex(from, s => s.Name.LocalName == "RemoveDir" && (string?)s.Attribute("Directories") == directory);

    private static bool IncludesUnder(XElement item, string directory) =>
        ((string?)item.Attribute("Include") ?? string.Empty).StartsWith(directory + "\\", StringComparison.Ordinal);

    private static bool ListsTarget(string dependsOnTargets, string name)
    {
        var rest = dependsOnTargets.AsSpan();
        while (rest.Length > 0)
        {
            var end = rest.IndexOf(';');
            var entry = (end < 0 ? rest : rest[..end]).Trim();
            if (entry.SequenceEqual(name.AsSpan()))
            {
                return true;
            }

            rest = end < 0 ? [] : rest[(end + 1)..];
        }

        return false;
    }

    private static string Name(XElement target) => (string?)target.Attribute("Name") ?? string.Empty;
}
