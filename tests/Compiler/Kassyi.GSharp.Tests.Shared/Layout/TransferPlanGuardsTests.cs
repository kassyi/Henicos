#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TransferPlannerTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class TransferPlanGuardsTests
{
    // The launch-time layout checks built from the plans are the ones KernelHostModel collects from the symbols
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void TransferPlanGuards_EqualTheLayoutGuardsOfKernelHostModel()
    {
        var launches = 0;
        var guardCount = 0;
        foreach (var method in EnumerateLaunchMethods())
        {
            var model = KernelHostModel.Create(method, default);
            foreach (var (dialect, expected) in new[]
            {
                (HostDialects.Cuda, model.CudaLayoutGuards),
                (HostDialects.Cpu, model.CpuLayoutGuards)
            })
            {
                var plans = method.Parameters.Select(p => TransferPlanner.Plan(p.Type, dialect)).ToList();
                if (!method.ReturnsVoid)
                {
                    plans.Add(TransferPlanner.Plan(method.ReturnType, dialect));
                }

                var actual = TransferPlanGuards.Collect([.. plans], method.ContainingAssembly.Name);
                Assert.True(
                    expected == actual,
                    $"{method.ContainingType?.Name}.{method.Name} ({dialect.GetType().Name}):{Environment.NewLine}" +
                    $"expected {expected}{Environment.NewLine}actual   {actual}");
                launches++;
                guardCount += actual.Length;
            }
        }

        Assert.True(launches > 30, $"only {launches} launches were compared");
        Assert.True(guardCount > 40, $"only {guardCount} guards were compared");
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void TransferPlanGuards_NameOnlyTheFieldsALauncherCanName()
    {
        var symbol = GetLayoutCaseStruct(nameof(AccessMix));
        var plan = Assert.IsType<TransferPlan.Blit>(TransferPlanner.Plan(symbol, HostDialects.Cpu));
        var own = symbol.ContainingAssembly.Name;

        var guards = TransferPlanGuards.Collect([plan], own);
        Assert.Equal(new[] { "A", "B" }, Assert.Single(guards).Fields.Select(static f => f.Name));
        Assert.Equal(
            new[] { "A" },
            Assert.Single(TransferPlanGuards.Collect([plan], "Other")).Fields.Select(static f => f.Name));

        // _c is private, P a pointer, D an auto-property: their offsets are left to the size check
        Assert.Equal(5, plan.Members.Length);
        Assert.All(
            plan.Members.Where(static m => m.Name is "_c" or "P" or "D"),
            static m => Assert.Equal(MemberAccess.NotNameable, m.Access));
    }
}
