using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Kassyi.GSharp.Tests.Transpiler;

public class PoolStressTests
{
    [Fact]
    [Trait("FeatureId", "GEN-09")]
    [Trait("Category", "Stress")]
    public void RoslynToCudaVisitor_PoolIsThreadSafeAndResetsProperly()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
                class Dummy { 
                    void M() { } 
                }
            """);
        var compilation = CSharpCompilation.Create("DummyCompilation", new[] { syntaxTree });
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var exceptions = new ConcurrentBag<Exception>();

        // High load to force pool contention
        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                using var visitor = RoslynToCudaVisitor.Rent(semanticModel, null);

                Assert.Empty(visitor._context.UsedStructs);
                Assert.Empty(visitor._context.LocalVariableOverrides);

                // Mutate to test reset in future loops
                visitor._context.LocalVariableOverrides[$"var_{i}"] = new OverrideValue("test");
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    [Trait("FeatureId", "ANZ-09")]
    [Trait("Category", "Stress")]
    public void GpuKernelValidationWalker_PoolIsThreadSafeAndResetsProperly()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                var dummyContext = new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(null!, null!, null!);
                using var walker = GpuKernelValidationWalker.Rent(dummyContext);

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
