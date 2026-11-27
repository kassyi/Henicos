using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the plan of the shadow struct of a <c>Nullable&lt;T&gt;</c> (#961): its device layout, how it holds the
/// value and the methods it calls, without reading generated text.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class NullableShadowPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/CpuNativeArgumentSource.cs")
    );

    // Vector2 is 8-aligned on CUDA and 4-aligned on the CPU: the value sits at 8 in 16 bytes, or at 4 in 12
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(true, 16, 8)]
    [InlineData(false, 12, 4)]
    public void Create_Vector2_PlacesTheValueAtItsDeviceAlignment(bool cuda, int size, int offset)
    {
        var plan = NullableShadowPlan.Create(MakeNullable("System.Numerics.Vector2"), Dialect(cuda));

        Assert.Equal(size, plan.DeviceSize);
        Assert.Equal(offset, plan.ValueOffset);
        Assert.Equal(8, plan.ValueSize);
        Assert.Equal(NullableValueForm.Bytes, plan.ValueForm);
        Assert.Equal("__gsharp_nullable_of_Vector2", plan.ShadowName);
        Assert.Equal("Marshal___gsharp_nullable_of_Vector2", plan.MarshalMethodName);
        Assert.Null(plan.ValueMarshalMethodName);
        Assert.Null(plan.ArrayHelpers);
    }

    // A user struct value goes through its own shadow; one that holds arrays gets the CPU array helpers
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_StructHoldingAnArray_CallsTheValueShadow(bool cuda)
    {
        var plan = NullableShadowPlan.Create(MakeNullable("Native.Holder"), Dialect(cuda));

        Assert.Equal(NullableValueForm.Shadow, plan.ValueForm);
        Assert.Equal("AotContexts.Holder", plan.ValueShadowPath);
        Assert.Equal("Marshal_Holder", plan.ValueMarshalMethodName);
        Assert.Equal("Unmarshal_Holder", plan.ValueUnmarshalMethodName);
        Assert.Equal(8, plan.ValueOffset);
        Assert.Equal(cuda ? null : "CopyArrays_Holder", plan.ArrayHelpers?.ValueCopyArraysMethodName);
    }

    // A primitive value is a field of its own type
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_Int32_HoldsTheValueAsAField()
    {
        var plan = NullableShadowPlan.Create(
            s_compilation.GetSpecialType(SpecialType.System_Nullable_T)
                .Construct(s_compilation.GetSpecialType(SpecialType.System_Int32)),
            HostDialects.Cuda);

        Assert.Equal(NullableValueForm.Named, plan.ValueForm);
        Assert.Equal(8, plan.DeviceSize);
        Assert.Equal(4, plan.ValueOffset);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_NullableNestedViews_PlansInnerElementWalk()
    {
        var nullableType = s_compilation.GetSpecialType(SpecialType.System_Nullable_T);
        var intType = s_compilation.GetSpecialType(SpecialType.System_Int32);
        var memoryDef = s_compilation.GetTypeByMetadataName("System.Memory`1");
        Assert.NotNull(memoryDef);

        var memInt = memoryDef.Construct(intType);
        var intArray = s_compilation.CreateArrayTypeSymbol(intType);
        var nullableMemInt = nullableType.Construct(memInt);

        var memOfMemPlan = NullableShadowPlan.Create(nullableType.Construct(memoryDef.Construct(memInt)), HostDialects.Cuda);
        var memOfArrPlan = NullableShadowPlan.Create(nullableType.Construct(memoryDef.Construct(intArray)), HostDialects.Cuda);
        var memOfOptPlan = NullableShadowPlan.Create(nullableType.Construct(memoryDef.Construct(nullableMemInt)), HostDialects.Cuda);

        var outerMemOfMem = Assert.IsType<NullableHeldView>(memOfMemPlan.HeldArrays?.Value);
        Assert.IsType<NullableHeldView>(outerMemOfMem.Element);
        Assert.Equal("__gsharp_nullable_of_Memory_Memory_Int32", memOfMemPlan.ShadowName);

        var longType = s_compilation.GetSpecialType(SpecialType.System_Int64);
        var memOfMemLongPlan = NullableShadowPlan.Create(
            nullableType.Construct(memoryDef.Construct(memoryDef.Construct(longType))),
            HostDialects.Cuda);
        Assert.Equal("__gsharp_nullable_of_Memory_Memory_Int64", memOfMemLongPlan.ShadowName);

        var outerMemOfArr = Assert.IsType<NullableHeldView>(memOfArrPlan.HeldArrays?.Value);
        Assert.IsType<NullableHeldArray>(outerMemOfArr.Element);

        var outerMemOfOpt = Assert.IsType<NullableHeldView>(memOfOptPlan.HeldArrays?.Value);
        Assert.IsType<NullableHeldMethod>(outerMemOfOpt.Element);

        var deepOptPlan = NullableShadowPlan.Create(
            nullableType.Construct(memoryDef.Construct(memoryDef.Construct(nullableMemInt))),
            HostDialects.Cuda);
        var outerDeepOpt = Assert.IsType<NullableHeldView>(deepOptPlan.HeldArrays?.Value);
        var midDeepOpt = Assert.IsType<NullableHeldView>(outerDeepOpt.Element);
        var leafDeepOpt = Assert.IsType<NullableHeldMethod>(midDeepOpt.Element);
        Assert.Equal("AliasHeldArrays___gsharp_nullable_of_Memory_Int32", leafDeepOpt.MethodName);

        var holder = s_compilation.GetTypeByMetadataName("Native.Holder");
        Assert.NotNull(holder);
        var deepHolderPlan = NullableShadowPlan.Create(
            nullableType.Construct(memoryDef.Construct(memoryDef.Construct(holder))),
            HostDialects.Cuda);
        var outerDeepHolder = Assert.IsType<NullableHeldView>(deepHolderPlan.HeldArrays?.Value);
        var midDeepHolder = Assert.IsType<NullableHeldView>(outerDeepHolder.Element);
        var leafDeepHolder = Assert.IsType<NullableHeldMethod>(midDeepHolder.Element);
        Assert.Equal("AliasHeldArrays_Holder", leafDeepHolder.MethodName);
    }

    private static ITranspilerDialect Dialect(bool cuda) => cuda ? HostDialects.Cuda : HostDialects.Cpu;

    private static INamedTypeSymbol MakeNullable(string valueMetadataName)
    {
        var value = s_compilation.GetTypeByMetadataName(valueMetadataName);
        Assert.NotNull(value);
        return s_compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(value);
    }
}
