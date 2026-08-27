using System.Collections.Concurrent;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_15_PoolStressTests
{
    private const string ID = nameof(GEN_15_PoolStressTests);

    public static readonly string GEN15_Sources = $$"""
    class Dummy_{{ID}} { public void M() { } }
    """;

    [ModuleInitializer]
    public static void Register() => Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.RegisterSource(GEN15_Sources);

    [Fact]
    [Trait("FeatureId", "GEN-15")]
    [Trait("Category", "Stress")]
    public void RoslynToCudaVisitor_PoolIsThreadSafeAndResetsProperly()
    {
        var context = Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "M");
        var semanticModel = context.Model;

        var exceptions = new ConcurrentBag<Exception>();

        // High load to force pool contention
        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                using var visitor = RoslynToCudaVisitor.Rent(semanticModel, null);

                Assert.Empty(visitor.Context.UsedStructs);
                Assert.Empty(visitor.Context.LocalVariableOverrides);

                // Mutate to test reset in future loops
                visitor.Context.LocalVariableOverrides[$"var_{i}"] = new OverrideValue("test");
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    [Trait("FeatureId", "GEN-15")]
    [Trait("Category", "Stress")]
    public void GpuKernelValidationWalker_PoolIsThreadSafeAndResetsProperly()
    {
        var context = Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "M");
        var semanticModel = context.Model;
        var methodSymbol = context.MethodSymbol;
        var methodDecl = (Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                var dummyContext = new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(semanticModel, methodSymbol, _ => { });
                using var walker = GpuKernelValidationWalker.Rent(dummyContext);
                
                walker.Visit(methodDecl);

                // Use reflection to ensure _context is set
                var field = typeof(GpuKernelValidationWalker).GetField("_context",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var ctx = field!.GetValue(walker);
                Assert.NotNull(ctx);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
