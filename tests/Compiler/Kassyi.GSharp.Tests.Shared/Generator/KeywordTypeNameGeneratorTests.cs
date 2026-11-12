using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The generated managed C# spells a type whose name is a C# keyword with <c>@</c>, as C# source does: the type-key
/// scope around the creation of a generic struct that declares shared memory names <c>@class&lt;int&gt;</c> and a
/// keyword type argument, and the files compile.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class KeywordTypeNameGeneratorTests
{
    private static readonly Lazy<(GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)>
        s_run = new(static () =>
            RoslynTestUtility.RunGenerator(RoslynTestUtility.ReadTestCase("Generators/KeywordTypeNames.cs"), "Cpu"));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GeneratedFiles_KeywordTypeNames_Compile()
    {
        var errors = s_run.Value.Diagnostics
            .Concat(s_run.Value.Output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("EndTypeKeyScope<global::KeywordTypeNames.@class<int>>")]
    [InlineData("EndTypeKeyScope<global::KeywordTypeNames.Slot<global::KeywordTypeNames.@struct>>")]
    public void TypeKeyScope_KeywordTypeName_IsEscaped(string expected)
    {
        var generated = string.Concat(s_run.Value.Driver.GetRunResult().GeneratedTrees
            .Select(static tree => tree.GetText().ToString()));

        Assert.Contains(expected, generated, StringComparison.Ordinal);
    }
}
