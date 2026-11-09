#nullable enable
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TransferPlannerTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class TransferPlannerMemberTests
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ArrayFieldAndNullableMembers_HaveTheirOwnPlans()
    {
        var cuda = HostDialects.Cuda;

        var arrays = Assert.IsType<TransferPlan.Convert>(
            TransferPlanner.Plan(GetLayoutCaseStruct(nameof(DirectArrayFieldStruct)), cuda));
        Assert.Null(arrays.HostSize);
        var values = arrays.Members.First(static m => m.Name == nameof(DirectArrayFieldStruct.Values));
        Assert.Null(values.HostOffset);
        var view = Assert.IsType<TransferPlan.ArrayView>(values.Plan);
        Assert.Equal(TransferPlan.ArrayView.HostProxySize, view.HostSize);
        Assert.Equal(cuda.ArrayViewLayout.Size, view.DeviceSize);
        // A leaf has an empty member list, not a default one, so it equals a plan written with []
        Assert.Equal(
            new TransferPlan.Blit(4, "global::System.Int32", null, ImmutableArray<MemberPlan>.Empty.AsEquatableArray()),
            view.Element);

        var nullables = TransferPlanner.Plan(GetLayoutCaseStruct(nameof(NullableFieldsStruct)), cuda);
        var members = Assert.IsType<TransferPlan.Convert>(nullables).Members;
        var float2 = members.First(static m => m.Name == nameof(NullableFieldsStruct.NullableFloat2));
        var nullable = Assert.IsType<TransferPlan.Nullable>(float2.Plan);
        // Float2 is 4-aligned on the host (12 bytes in all) and 8-aligned on CUDA (16 bytes, the value at 8)
        Assert.Equal(Unsafe.SizeOf<Float2?>(), nullable.HostSize);
        Assert.Equal(16, nullable.DeviceSize);
        Assert.Equal(8, nullable.ValueDeviceOffset);
    }

    // A struct with an array of its own type is converted, as the layout model says, and not reported
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_StructWithArrayOfItself_RefersBackToTheEnclosingPlan()
    {
        var symbol = GetLayoutCaseStruct(nameof(TreeNode));
        foreach (var dialect in s_dialects)
        {
            Assert.Equal(TransferClass.Convert, TypeLayoutCalculator.GetStructLayout(symbol, dialect).Transfer);

            var plan = Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(symbol, dialect));
            var children = plan.Members.First(static m => m.Name == nameof(TreeNode.Children));
            var reference = Assert.IsType<TransferPlan.StructRef>(
                Assert.IsType<TransferPlan.ArrayView>(children.Plan).Element);
            Assert.Equal(plan.CSharpType, reference.CSharpType);
            Assert.Equal(plan.ShadowPath, reference.ShadowPath);
            Assert.Equal(plan.DeviceSize, reference.DeviceSize);
        }
    }

    // Nullable<T> converts through HasValue and GetValueOrDefault (design Q2 a), so an auto-layout T crosses as its
    // own Convert plan; only the host size, which the runtime decides, is unknown
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NullableOfAnAutoLayoutStruct_IsNullableWithARuntimeHostSize()
    {
        var (compilation, _, _) = GetContext(s_transferPlanCases);
        var autoPair = GetTransferPlanCase(compilation, "AutoPair");
        foreach (var dialect in s_dialects)
        {
            var nullable = Assert.IsType<TransferPlan.Nullable>(
                TransferPlanner.Plan(MakeNullable(compilation, autoPair), dialect));
            Assert.Null(nullable.HostSize);
            Assert.Equal(TransferPlanner.Plan(autoPair, dialect), nullable.Value);
            Assert.IsType<TransferPlan.Convert>(nullable.Value);
            Assert.Equal(8, nullable.ValueDeviceOffset);
            Assert.Equal(24, nullable.DeviceSize);

            var holder = Assert.IsType<TransferPlan.Convert>(
                TransferPlanner.Plan(GetTransferPlanCase(compilation, "AutoPairHolder"), dialect));
            Assert.Equal(nullable, holder.Members.Single(static m => m.Name == "Item").Plan);
        }
    }

    // The Nullable's host size is the runtime's when the value's layout is known
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_NullableOfASequentialValue_HasTheRuntimeHostSize()
    {
        var plan = Assert.IsType<TransferPlan.Convert>(
            TransferPlanner.Plan(GetLayoutCaseStruct(nameof(NullableFieldsStruct)), HostDialects.Cuda));
        int?[] expected = [Unsafe.SizeOf<int?>(), Unsafe.SizeOf<double?>(), Unsafe.SizeOf<Float2?>()];
        Assert.Equal(
            expected,
            plan.Members.Select(static m => m.Plan).OfType<TransferPlan.Nullable>().Select(static n => n.HostSize));
    }
}
