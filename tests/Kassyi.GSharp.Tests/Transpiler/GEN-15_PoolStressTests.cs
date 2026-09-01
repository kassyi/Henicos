using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Kassyi.GSharp.Compiler.Dialects;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_15_PoolStressTests
{
    [Fact]
    [Trait("FeatureId", "GEN-15")]
    [Trait("Category", "Stress")]
    public void RoslynToCudaVisitor_PoolIsThreadSafeAndResetsProperly()
    {
        var context = MegaCompilationCache.GetMethodContext<Dummy>("M");
        var semanticModel = context.Model;

        var exceptions = new ConcurrentBag<Exception>();

        // High load to force pool contention
        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                using var visitor = RoslynToCudaVisitor.Rent(new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect(), semanticModel, null);

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
        var context = MegaCompilationCache.GetMethodContext<Dummy>("M");
        var semanticModel = context.Model;
        var methodSymbol = context.MethodSymbol;
        var methodDecl =
            (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0]
                .GetSyntax();

        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                var dummyContext = new ValidationContext(semanticModel,
                    methodSymbol, _ =>
                    {
                    });
                using var walker = GpuKernelValidationWalker.Rent(dummyContext);

                walker.Visit(methodDecl);

                // Ensure context is set properly
                Assert.NotNull(walker.Context.SemanticModel);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}

class Dummy
{
    public void M()
    {
    }
}
