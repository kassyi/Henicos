#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler.Managed;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Transpiles methods for the CPU with the split pass of the IR in front of the syntax passes
/// (<see cref="AstLoweringPipeline.WithIrSplitLowering"/>) and compares the result with the syntax passes alone: the
/// failures the transpiler reports and where (generator-invariants: lowered trees keep their original spans), and the
/// output of a compound assignment of each operator whose target the IR reads into a temporary.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrSplitLoweringOutputTests(ITestOutputHelper output)
{
    private const string OrderKernels = "Kassyi.GSharp.Tests.Shared.Differential.SplitOperandOrderDifferentialKernels";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryWrittenMethod_TranspiledForTheCpuWithTheIr_ReportsTheSameFailuresAtTheSamePlaces()
    {
        var differences = new ConcurrentBag<string>();
        var methods = 0;
        var failures = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (body, symbol, _) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                var model = compilation.GetSemanticModel(body.SyntaxTree);
                AstLoweringPipeline.HoistSplitPointsWithIr((CSharpSyntaxNode)body, !symbol.ReturnsVoid, model, symbol,
                    out var outcomes);
                if (body.Parent is not MethodDeclarationSyntax declaration || !outcomes.Any(static o => o.IsLowered))
                {
                    continue;
                }

                Interlocked.Increment(ref methods);
                var without = Failures(declaration, model, symbol);
                string[] with;
                using (AstLoweringPipeline.WithIrSplitLowering())
                {
                    with = Failures(declaration, model, symbol);
                }

                Interlocked.Add(ref failures, without.Length);
                if (!without.SequenceEqual(with, StringComparer.Ordinal))
                {
                    differences.Add($"{IrCoverageInputs.Where(body.SyntaxTree, symbol)}\n" +
                                    $"    without the IR: {string.Join(", ", without)}\n" +
                                    $"    with the IR:    {string.Join(", ", with)}");
                }
            }
        }, everyKernelFile: true);

        output.WriteLine($"{methods} methods with statements written by the IR transpiled, " +
                         $"{failures} failures without the IR");
        Assert.True(methods > 50, $"Only {methods} methods with statements written by the IR.");
        Assert.True(differences.IsEmpty,
            string.Join("\n", differences.OrderBy(static d => d, StringComparer.Ordinal).Take(20)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task CompoundOrderKernel_TranspiledForTheCpuWithTheIr_WritesEachOperatorOnItsTemporary()
    {
        var (declaration, model, symbol) = Method(OrderKernels, "CompoundOrderKernel");
        AstLoweringPipeline.HoistSplitPointsWithIr(declaration.Body ?? throw new InvalidOperationException(), false,
            model, symbol, out var outcomes);
        var failures = new List<TranspileFailure>();
        string native;
        string managed;
        using (AstLoweringPipeline.WithIrSplitLowering())
        {
            native = MethodTranspiler.GenerateCudaSource(declaration, model, failures.Add, isDevice: false,
                new StandardCppDialect(), out _);
            var plan = MethodEmissionPlan.Create(declaration, model, isKernel: true);
            managed = ManagedCSharpTranspiler.Generate(in plan);
        }

        Assert.All(outcomes, static outcome => Assert.True(outcome.IsLowered));
        Assert.True(outcomes.Count >= 12, $"Only {outcomes.Count} statements written by the IR.");
        Assert.Empty(failures);
        return Verifier.Verify($"// native\n{native}\n// managed\n{managed}", "txt").UseFastHashSnapshotName(
            nameof(CompoundOrderKernel_TranspiledForTheCpuWithTheIr_WritesEachOperatorOnItsTemporary));
    }

    // The failures of the CPU C++ of a method, each as its ID and span ("none" without an original span), sorted
    private static string[] Failures(MethodDeclarationSyntax declaration, SemanticModel model, IMethodSymbol symbol)
    {
        var failures = new List<TranspileFailure>();
        MethodTranspiler.GenerateCudaSource(declaration, model, failures.Add,
            isDevice: !symbol.HasAttribute(KnownAttributes.GpuKernel), new StandardCppDialect(), out _);
        return failures
            .Select(static failure => failure.Descriptor.Id + " " +
                                      (failure.Location.IsInSource ? failure.Location.SourceSpan.ToString() : "none"))
            .OrderBy(static failure => failure, StringComparer.Ordinal)
            .ToArray();
    }

    private static (MethodDeclarationSyntax Declaration, SemanticModel Model, IMethodSymbol Symbol) Method(
        string type, string name)
    {
        var compilation = RoslynTestUtility.SharedCompilation;
        var symbol = compilation.GetTypeByMetadataName(type)?.GetMembers(name).OfType<IMethodSymbol>().Single() ??
                     throw new InvalidOperationException($"{type}.{name} not found.");
        var declaration = symbol.DeclaringSyntaxReferences[0].GetSyntax() as MethodDeclarationSyntax ??
                          throw new InvalidOperationException($"{type}.{name} has no declaration.");
        return (declaration, compilation.GetSemanticModel(declaration.SyntaxTree), symbol);
    }
}
