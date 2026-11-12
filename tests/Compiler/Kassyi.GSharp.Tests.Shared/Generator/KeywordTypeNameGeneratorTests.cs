using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The generated C# spells a name that is a C# keyword with <c>@</c>, as C# source does: the type-key scope around the
/// creation of a generic struct that declares shared memory names <c>@class&lt;int&gt;</c> and a keyword type argument;
/// a kernel class in the namespace <c>@event.P3</c>, a kernel class named <c>@class</c>, a kernel class nested in
/// <c>@operator</c> and the native shadow struct of a parameter struct named <c>@struct</c> are declared under their
/// escaped names; and the files compile on every dialect.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class KeywordTypeNameGeneratorTests
{
    private static readonly Lazy<(GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)>
        s_cpuRun = new(static () => Run("Cpu"));

    private static readonly Lazy<(GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)>
        s_cudaRun = new(static () => Run("Cuda"));

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void GeneratedFiles_KeywordNames_Compile(string dialect)
    {
        var run = GetRun(dialect);
        var errors = run.Diagnostics
            .Concat(run.Output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("EndTypeKeyScope<global::KeywordTypeNames.@class<int>>")]
    [InlineData("EndTypeKeyScope<global::KeywordTypeNames.Slot<global::KeywordTypeNames.@struct>>")]
    [InlineData("new global::@event.P3.Slot<int>(")]
    public void ManagedBody_KeywordName_IsEscaped(string expected) =>
        Assert.Contains(expected, GetGeneratedText("Cpu"), StringComparison.Ordinal);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", "namespace @event.P3")]
    [InlineData("Cuda", "namespace @event.P3")]
    [InlineData("Cpu", "public partial class @class")]
    [InlineData("Cuda", "public partial class @class")]
    [InlineData("Cpu", "partial class @operator")]
    [InlineData("Cuda", "partial class @operator")]
    [InlineData("Cpu", "public unsafe partial struct @struct")]
    [InlineData("Cuda", "public unsafe partial struct @struct")]
    public void Declaration_KeywordName_IsEscaped(string dialect, string expected) =>
        Assert.Contains(expected, GetGeneratedText(dialect), StringComparison.Ordinal);

    private static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(
        string dialect) =>
        RoslynTestUtility.RunGenerator(
            RoslynTestUtility.CreateCompilation(
                RoslynTestUtility.ReadTestCase("Generators/KeywordTypeNames.cs"),
                RoslynTestUtility.ReadTestCase("Generators/KeywordNamespaceKernels.cs")
            ),
            dialect
        );

    private static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) GetRun(
        string dialect) =>
        dialect == "Cuda" ? s_cudaRun.Value : s_cpuRun.Value;

    private static string GetGeneratedText(string dialect) =>
        string.Concat(GetRun(dialect).Driver.GetRunResult().GeneratedTrees
            .Select(static tree => tree.GetText().ToString()));
}
