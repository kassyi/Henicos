using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ModuleNameCollisionGeneratorTests
{
    // Module names that differ only in characters an identifier cannot hold get distinct fatbin fields (CS0102 when
    // each of them was replaced by '_')
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaImportGenerator_ModuleNamesDifferingInPunctuation_EachLaunchesItsOwnModule()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Generators/ModuleNameCollisionImports.cs"));
        var driver = CSharpGeneratorDriver.Create(
            generators: [new CudaImportGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)
        ).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var errors = diagnostics
            .Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var text = string.Concat(driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("LoadAotKernel(s_fatbin_a_x00002db.Value", text, StringComparison.Ordinal);
        Assert.Contains("LoadAotKernel(s_fatbin_a_ub.Value", text, StringComparison.Ordinal);
        Assert.Contains("LoadAotKernel(s_fatbin_a_x00002eb.Value", text, StringComparison.Ordinal);
        Assert.Contains("LoadAotKernel(s_fatbin_a_x00007bb_x00007d.Value", text, StringComparison.Ordinal);
        Assert.Contains("LoadAotKernel(s_fatbin_a_ub_u.Value", text, StringComparison.Ordinal);
    }
}
