#nullable enable
using System;
using System.IO;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TransferPlannerTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class TransferPlannerDiagnosticsTests
{
    // An explicit-layout union with a layout-converted arm is rejected with GSH0078 (#1006)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_ExplicitUnionWithLayoutConvertedArm_IsGsh0078()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Diagnostics", "StructLayoutReturnDiagnostics.cs"));
        var brokenUnion = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("StructLayoutReturnDiagnosticsTest.BrokenUnionResult"));
        foreach (var dialect in s_dialects)
        {
            var plan = Assert.IsType<TransferPlan.Unsupported>(TransferPlanner.Plan(brokenUnion, dialect));
            Assert.Equal("GSH0078", plan.DiagnosticId);
            Assert.Contains("field 'Shifted' at offset 0 overlaps field", plan.Reason, StringComparison.Ordinal);
            Assert.Contains("Shifted", plan.Reason, StringComparison.Ordinal);
        }
    }

    // A struct outside the user structs that has no device representation is GSH0078 with no field to name
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_DecimalOutsideAStruct_IsGsh0078WithoutAField()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var plan = Assert.IsType<TransferPlan.Unsupported>(
            TransferPlanner.Plan(compilation.GetSpecialType(SpecialType.System_Decimal), HostDialects.Cuda));

        Assert.Equal("GSH0078", plan.DiagnosticId);
        Assert.Equal("'decimal' has no device representation", plan.Reason);
        Assert.Null(plan.FieldPath);
        // The type is kept, so that a struct member of this type becomes GSH0079 naming it
        Assert.Equal("decimal", plan.FieldTypeName);
    }

    // A member whose own plan is unsupported (a Nullable, an array, an inline array of an unsupported type) makes the
    // enclosing struct unsupported, although the layout model accepts the struct; the field path runs from the struct
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("NullablePricedHolder", "GSH0079", "Item.Price", "decimal", "field 'Item' of type ")]
    [InlineData("NullableMisalignedHolder", "GSH0078", null, null, "field 'Item' of type ")]
    [InlineData("NullableBagHolder", "GSH0078", null, null, "field 'Item' of type ")]
    [InlineData("DecimalArrayHolder", "GSH0079", "Items", "decimal", "field 'Items' of type 'decimal[]': ")]
    [InlineData("PricedArrayHolder", "GSH0079", "Items.Price", "decimal", "field 'Items' of type ")]
    [InlineData("NestedHolder", "GSH0079", "Holder.Item.Price", "decimal", "field 'Holder' of type ")]
    [InlineData("PricedOptionsHolder", "GSH0079", "Options.Price", "decimal", "field 'Options' of type ")]
    public void Plan_MemberWithAnUnsupportedPlan_MakesTheStructUnsupported(string name, string diagnosticId,
        string? fieldPath, string? fieldTypeName, string reasonStart)
    {
        var symbol = GetTransferPlanCase(name);
        foreach (var dialect in s_dialects)
        {
            // The layout model alone does not see the problem
            Assert.Null(TypeLayoutCalculator.GetStructLayout(symbol, dialect).Unsupported);

            var plan = Assert.IsType<TransferPlan.Unsupported>(TransferPlanner.Plan(symbol, dialect));
            Assert.Equal(diagnosticId, plan.DiagnosticId);
            Assert.Equal(fieldPath, plan.FieldPath);
            Assert.Equal(fieldTypeName, plan.FieldTypeName);
            Assert.StartsWith(reasonStart, plan.Reason, StringComparison.Ordinal);
        }
    }

    // An array or a Nullable of a type that cannot cross is that type's plan, not an ArrayView or Nullable around it,
    // so that a check of the outermost plan finds it
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_ArrayOrNullableOfAnUnsupportedType_IsTheUnsupportedPlanOfThatType()
    {
        var (compilation, _, _) = GetContext(s_transferPlanCases);
        var priced = GetTransferPlanCase(compilation, "Priced");
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        foreach (var dialect in s_dialects)
        {
            var pricedPlan = Assert.IsType<TransferPlan.Unsupported>(TransferPlanner.Plan(priced, dialect));
            Assert.Equal("Price", pricedPlan.FieldPath);

            Assert.Equal(pricedPlan, TransferPlanner.Plan(compilation.CreateArrayTypeSymbol(priced), dialect));
            Assert.Equal(pricedPlan, TransferPlanner.Plan(MakeNullable(compilation, priced), dialect));
            Assert.Equal(TransferPlanner.Plan(decimalType, dialect),
                TransferPlanner.Plan(compilation.CreateArrayTypeSymbol(decimalType), dialect));
        }
    }

    // A Nullable whose value holds a managed reference (an array field) stays GSH0078 (design N4, section 8 step 5)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_NullableOfAStructHoldingAnArray_IsGsh0078()
    {
        var (compilation, _, _) = GetContext(s_transferPlanCases);
        var bag = GetTransferPlanCase(compilation, "Bag");
        foreach (var dialect in s_dialects)
        {
            Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(bag, dialect));
            var plan = Assert.IsType<TransferPlan.Unsupported>(
                TransferPlanner.Plan(MakeNullable(compilation, bag), dialect));
            Assert.Equal("GSH0078", plan.DiagnosticId);
            Assert.Null(plan.FieldPath);
            Assert.Null(plan.FieldTypeName);
        }
    }
}
