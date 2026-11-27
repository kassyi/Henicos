#nullable enable
namespace Kassyi.GSharp.Tests.Cpu.Backends.Cpp;

/// <summary>
/// Guards the IR C++ writer folder (<c>src/Compiler/Kassyi.GSharp.Compiler/Backends/Cpp/</c>) against Roslyn syntax /
/// semantic-model leakage and dialect-capability checks in its <c>Ir*.cs</c> / <c>IIr*.cs</c> sources.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrCppSourceBoundaryTests
{
    private static readonly string[] s_forbiddenWords =
    [
        "SemanticModel",
        "SyntaxNode",
        "IOperation",
        "TranspileContext",
        "GetOperation",
        "Visitor",
        "SyntaxFactory",
        "IsCuda",
        "RunsThreadsOnOwnStacks",
        "EmulatesThreadsWithStateMachine",
        "Capabilities"
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BackendsCppIrFiles_DoNotContainForbiddenWords()
    {
        var root = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Backends", "Cpp");
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
            {
                var name = Path.GetFileName(path);
                return name.StartsWith("Ir", StringComparison.Ordinal) ||
                       name.StartsWith("IIr", StringComparison.Ordinal);
            })
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);
        Assert.DoesNotContain(files, static path => Path.GetFileName(path) == "CppBodyProfile.cs");

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(root, file);
            foreach (var word in s_forbiddenWords)
            {
                if (text.Contains(word, StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: {word}");
                }
            }
        }

        Assert.Empty(violations);
    }
}
