#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TransferPlannerTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

// The transfer plan against the runtime itself and against the layout model it replaces: every struct of
// TestCases/Layout is planned under both dialects, the host size and offsets are compared with Unsafe.SizeOf and the
// offsets the CLR gives the fields, and the kinds, diagnostics and launch-time layout checks with the current ones
public class TransferPlannerTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_EveryLayoutCaseStruct_MatchesTheRuntimeHostLayout()
    {
        var failures = new List<string>();
        var checkedOffsets = 0;

        foreach (var (symbol, runtimeType) in EnumerateLayoutCaseStructs())
        {
            foreach (var dialect in s_dialects)
            {
                var plan = TransferPlanner.Plan(symbol, dialect);
                var where = $"{symbol.ToDisplayString()} ({dialect.GetType().Name})";

                if (plan is TransferPlan.Unsupported unsupported)
                {
                    failures.Add($"{where}: unexpectedly unsupported: {unsupported.Reason}");
                    continue;
                }

                var hostIsRuntimeDecided = ContainsReferences(runtimeType) ||
                                           runtimeType.StructLayoutAttribute?.Value == LayoutKind.Auto;
                if (plan.HostSize is not { } hostSize)
                {
                    if (!hostIsRuntimeDecided)
                    {
                        failures.Add($"{where}: the host layout is unknown, but the CLR lays it out sequentially");
                    }

                    continue;
                }

                if (hostIsRuntimeDecided)
                {
                    failures.Add($"{where}: the plan predicts a host layout that the CLR decides at run time");
                    continue;
                }

                var runtimeSize = SizeOf(runtimeType);
                if (hostSize != runtimeSize)
                {
                    failures.Add($"{where}: host size {hostSize}, runtime {runtimeSize}");
                }

                foreach (var member in GetMembers(plan))
                {
                    var runtimeOffset = OffsetOf(runtimeType, member.Name);
                    checkedOffsets++;
                    if (member.HostOffset != runtimeOffset)
                    {
                        failures.Add(
                            $"{where}: field '{member.Name}' at host offset {member.HostOffset}, " +
                            $"runtime {runtimeOffset}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // The check above is reached: the cases have well over a hundred fields with a predicted offset
        Assert.True(checkedOffsets > 100, $"only {checkedOffsets} offsets were compared");
    }

    // The plan of every user struct, and of each of its members down the tree, has the kind that the current layout
    // model gives the same type; an unsupported one carries the same diagnostic, field path and field type
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_UserStructs_HaveTheKindOfTheCurrentTransferClass()
    {
        var failures = new List<string>();
        var kinds = new HashSet<Type>();
        var compared = 0;
        foreach (var symbol in EnumerateLayoutCaseStructs()
            .Select(static c => c.Symbol)
            .Concat(EnumerateUnsupportedCaseStructs()))
        {
            Assert.True(TypeLayoutCalculator.IsUserStruct(symbol), symbol.ToDisplayString());
            foreach (var dialect in s_dialects)
            {
                var plan = TransferPlanner.Plan(symbol, dialect);
                kinds.Add(plan.GetType());
                compared += ComparePlan(
                    plan, symbol, dialect, [], $"{symbol.ToDisplayString()} ({dialect.GetType().Name})", failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(compared > 300, $"only {compared} plans were compared");
        Assert.Contains(typeof(TransferPlan.Blit), kinds);
        Assert.Contains(typeof(TransferPlan.Convert), kinds);
        Assert.Contains(typeof(TransferPlan.Unsupported), kinds);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_VectorStruct_IsBlitOnTheCpuAndConvertOnCuda()
    {
        var symbol = GetLayoutCaseStruct(nameof(SystemNumericsVectorsStruct));

        var cpu = Assert.IsType<TransferPlan.Blit>(TransferPlanner.Plan(symbol, HostDialects.Cpu));
        var cuda = Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(symbol, HostDialects.Cuda));

        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), cpu.Size);
        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), cuda.HostSize);
        var v2 = cuda.Members.First(static m => m.Name == nameof(SystemNumericsVectorsStruct.V2));
        Assert.Equal(4, v2.HostOffset);
        Assert.Equal(8, v2.DeviceOffset);
        Assert.IsType<TransferPlan.Blit>(v2.Plan);
    }

    // A member plan carries the member kind, which the emitters switch on once they read plans instead of symbols;
    // an [InlineArray(1)] element has the count of a plain field and is told apart only by the kind
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_Members_CarryTheirKind()
    {
        foreach (var dialect in s_dialects)
        {
            var plan = TransferPlanner.Plan(GetTransferPlanCase("MemberKinds"), dialect);
            var members = plan switch
            {
                TransferPlan.Blit blit => blit.Members,
                TransferPlan.Convert convert => convert.Members,
                _ => throw new InvalidOperationException(plan.ToString())
            };

            Assert.Equal(
                new[]
                {
                    // Roslyn declares the hidden field of a captured parameter first
                    ("captured", StructAbiMemberKind.CapturedParameter, 1), ("Field", StructAbiMemberKind.Field, 1),
                    ("Property", StructAbiMemberKind.AutoProperty, 1), ("Buffer", StructAbiMemberKind.FixedBuffer, 2),
                    ("Single", StructAbiMemberKind.Field, 1)
                },
                members.Select(static m => (m.Name, m.Kind, m.ElementCount)));

            var one = Assert.IsType<TransferPlan.Blit>(members.Single(static m => m.Name == "Single").Plan);
            var element = Assert.Single(one.Members);
            Assert.Equal(StructAbiMemberKind.InlineArrayElement, element.Kind);
            Assert.Equal(1, element.ElementCount);
        }
    }

    // The planner keeps no symbol: two compilations of the same source give equal plans, as the generator cache needs
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_SameSourceInTwoCompilations_GivesEqualPlans()
    {
        var source = RoslynTestUtility.ReadTestCase(Path.Combine("Layout", "HostLayoutCases.cs"));
        var first = RoslynTestUtility.CreateCompilation(source);
        var second = RoslynTestUtility.CreateCompilation(source);
        string[] names =
        [
            nameof(SimpleSequential), nameof(SystemNumericsVectorsStruct), nameof(DirectArrayFieldStruct),
            nameof(NullableFieldsStruct), nameof(StructWithDivergentInlineArray), nameof(TreeNode), nameof(AccessMix)
        ];

        foreach (var name in names)
        {
            var metadataName = $"Kassyi.GSharp.Tests.Shared.TestCases.Layout.{name}";
            var a = first.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName);
            var b = second.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName);
            Assert.NotSame(a, b);
            foreach (var dialect in s_dialects)
            {
                var planA = TransferPlanner.Plan(a, dialect);
                var planB = TransferPlanner.Plan(b, dialect);
                Assert.Equal(planA, planB);
                Assert.Equal(planA.GetHashCode(), planB.GetHashCode());
            }
        }
    }
}
