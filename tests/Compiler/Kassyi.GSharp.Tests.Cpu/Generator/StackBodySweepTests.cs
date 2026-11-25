#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

// Every kernel and device function of the shared test sources with Warp.ActiveMask() put in front of its body, built
// with the managed stack model: the stack form of each writes the body lowered for the stacks (the state machine's
// passes up to its reconvergence regions), and none of those forms may hold a C# error. The shared compilation has
// errors of its own (types of the test projects it does not reference); only the errors inside a stack form count.
// The run of the generator over the whole corpus holds about 2 GB, so the test runs one after another with the other
// whole-corpus tests.
[Collection(TestCollections.CorpusScan)]
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StackBodySweepTests
{
    private const string StackFormPrefix = "__gsharp_stackmodel_";

    // [WHY] #2320 (User-defined && / || whose right operand reaches a split point lose the & / | call in the shared
    // lowering): the lowering of such an operator writes !t for ||, which C# rejects for such a type, and the right
    // operand alone in place of the operator, for the state machine as for the stacks. The file stays out of the sweep
    // until #2320 is fixed
    private const string UserDefinedShortCircuitFile = "OperatorMacroConditionalKernels.cu.cs";

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SharedFunctionsWithActiveMask_StackModel_StackFormsCompile()
    {
        var (compilation, functions) =
            WithActiveMaskInEveryFunction((CSharpCompilation)RoslynTestUtility.SharedCompilation);
        Assert.True(functions > 2000, $"only {functions} kernels and device functions got Warp.ActiveMask()");

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu",
                    new Dictionary<string, string> { ["build_property.GSharpManagedBarrierModel"] = "Stack" }))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.Null(driver.GetRunResult().Results.Single().Exception);

        var stackForms = 0;
        var errors = new List<string>();
        foreach (var tree in output.SyntaxTrees.Skip(compilation.SyntaxTrees.Length))
        {
            var forms = tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(static m => m.Identifier.ValueText.StartsWith(StackFormPrefix, StringComparison.Ordinal))
                .ToArray();
            if (forms.Length == 0)
            {
                continue;
            }

            stackForms += forms.Length;
            var model = output.GetSemanticModel(tree);
            foreach (var form in forms)
            {
                errors.AddRange(model.GetDiagnostics(form.Span)
                    .Where(static d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Id} {form.Identifier.ValueText}: {d.GetMessage()}"));
            }
        }

        Assert.True(stackForms > 1000, $"only {stackForms} stack forms were made");
        Assert.True(errors.Count == 0, $"{errors.Count} errors in stack forms:\n" + string.Join("\n", errors.Take(40)));
    }

    // The compilation with global::Kassyi.GSharp.Core.Warp.ActiveMask(); first in the block body of every kernel and
    // device function of a .cu.cs file, and the number of such functions
    private static (CSharpCompilation Compilation, int Functions) WithActiveMaskInEveryFunction(
        CSharpCompilation shared)
    {
        var mutated = shared;
        var count = 0;
        foreach (var tree in shared.SyntaxTrees)
        {
            if (!tree.FilePath.EndsWith(".cu.cs", StringComparison.OrdinalIgnoreCase) ||
                tree.FilePath.EndsWith(UserDefinedShortCircuitFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = shared.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var kernels = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Body is not null &&
                            model.GetDeclaredSymbol(m) is { } method &&
                            method.GetAttributes().Any(static a =>
                                a.AttributeClass?.Name is "GpuKernelAttribute" or "GpuDeviceFunctionAttribute"))
                .ToArray();
            if (kernels.Length == 0)
            {
                continue;
            }

            count += kernels.Length;
            var call = SyntaxFactory.ParseStatement("global::Kassyi.GSharp.Core.Warp.ActiveMask();");
            var newRoot = root.ReplaceNodes(kernels, (_, rewritten) => rewritten.Body is { } body
                ? rewritten.WithBody(body.WithStatements(body.Statements.Insert(0, call)))
                : rewritten);
            mutated = mutated.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(newRoot, tree.Options));
        }

        return (mutated, count);
    }
}
