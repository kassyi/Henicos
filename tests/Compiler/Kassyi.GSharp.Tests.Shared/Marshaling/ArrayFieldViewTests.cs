using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the plan of a struct field whose array-like type is not an array (#1267) and the shadow of its Nullable
/// (#1115): how the marshaling reaches the view's span, whether it is read-only, and which elements it converts.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ArrayFieldViewTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/CpuNativeArgumentSource.cs")
    );

    // Each container reaches its span and memory through its own member; the read-only ones are never written back
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Memory", "h.Memory.Span", "h.Memory", false)]
    [InlineData("ReadOnly", "h.ReadOnly.Span", "h.ReadOnly", true)]
    [InlineData("Segment", "global::System.MemoryExtensions.AsSpan(h.Segment)",
        "global::System.MemoryExtensions.AsMemory(h.Segment)", false)]
    [InlineData("Immutable", "h.Immutable.AsSpan()", "h.Immutable.AsMemory()", true)]
    public void Create_Container_ReachesTheSpanOfTheView(string field, string span, string memory, bool readOnly)
    {
        var view = ArrayFieldView.Create(FieldType(field), HostDialects.Cpu);

        Assert.Equal(span, view.SpanExpression("h." + field));
        Assert.Equal(memory, view.MemoryExpression("h." + field));
        Assert.Equal(readOnly, view.IsReadOnly);
        Assert.False(view.IsConverted);
    }

    // CUDA converts an element with a shadow struct, the CPU only one whose layouts differ there
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Create_ElementThatCudaLaysOutDifferently_IsConvertedOnCuda(bool cuda, bool converted)
    {
        var view = ArrayFieldView.Create(FieldType("Spreads"), cuda ? HostDialects.Cuda : HostDialects.Cpu);

        Assert.Equal(converted, view.IsConverted);
        Assert.Equal(converted ? "Marshal_Spread" : null, view.ElementMarshalMethodName);
    }

    // A Memory<int>? field goes through the shadow of its Nullable, keyed by its element, with the view of its value
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NullableShadowPlan_NullableMemory_HoldsTheValueAsAnArrayView()
    {
        var nullable = (INamedTypeSymbol)FieldType("Optional");
        var plan = NullableShadowPlan.Create(nullable, HostDialects.Cpu);

        Assert.Equal(NullableValueForm.ArrayView, plan.ValueForm);
        Assert.Equal("__gsharp_nullable_of_Memory_Int32", plan.ShadowName);
        Assert.Equal(8, plan.ValueOffset);
        Assert.Equal(24, plan.DeviceSize);
        Assert.Equal(ArrayContainerKind.Memory, plan.ArrayValue?.Container);
        Assert.Equal("CopyArrays___gsharp_nullable_of_Memory_Int32", plan.ArrayHelpers?.CopyArraysMethodName);
        Assert.Null(plan.ArrayHelpers?.HoldsArrayMethodName);
        Assert.True(HostShadowStructs.IsConverted(nullable, HostDialects.Cpu));
    }

    private static ITypeSymbol FieldType(string field)
    {
        var views = s_compilation.GetTypeByMetadataName("Native.Views");
        Assert.NotNull(views);
        return views.GetMembers(field).OfType<IFieldSymbol>().Single().Type;
    }
}
