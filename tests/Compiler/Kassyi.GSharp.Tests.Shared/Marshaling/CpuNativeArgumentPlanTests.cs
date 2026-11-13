using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the native argument plan of the CPU launcher: the type each kernel parameter has in the call of the native
/// launcher export and the form of the argument that passes it, without reading generated text.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CpuNativeArgumentPlanTests
{
    private const string ArrayProxy = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";
    private const string NullableArrayProxy = "global::System.Nullable<" + ArrayProxy + ">*";
    private const string Int32 = "global::System.Int32";
    private const string ShuffledShadow = "global::Native.Arguments.AotContexts.Shuffled";
    private const string HolderShadow = "global::Native.Arguments.AotContexts.Holder";

    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/CpuNativeArgumentSource.cs")
    );

    // Every array-like goes as a GSharpArrayProxy value built from its pinned data; only a Nullable<array-like> goes as
    // the address of a Nullable<GSharpArrayProxy>, so that the native launcher sees whether it has a value
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ArrayLikeParameters_PassProxies()
    {
        var plan = CreatePlan("ArrayKinds");

        Assert.Equal(
            [
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ArrayProxy),
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ArrayProxy),
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ArrayProxy),
                new CpuNativeArgument(NullableArrayProxy, CpuNativeArgumentForm.NullableProxyAddress)
            ],
            plan.Arguments.ToArray()
        );
    }

    // A ref / out scalar or a ref struct of the same layout goes as its pinned address; a ref struct whose layouts
    // differ as the address of its native shadow, typed as the shadow
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ReferenceParameters_PassAddresses()
    {
        var plan = CreatePlan("ReferenceKinds");

        Assert.Equal(
            [
                new CpuNativeArgument(Int32 + "*", CpuNativeArgumentForm.PinnedAddress),
                new CpuNativeArgument(Int32 + "*", CpuNativeArgumentForm.PinnedAddress),
                new CpuNativeArgument("global::Native.Pair*", CpuNativeArgumentForm.PinnedAddress),
                new CpuNativeArgument(ShuffledShadow + "*", CpuNativeArgumentForm.ShadowAddress)
            ],
            plan.Arguments.ToArray()
        );
    }

    // A struct by value goes by address (#511): the host value when the layouts agree, the native shadow when an
    // automatic layout or an array field makes them differ; a scalar goes as its value
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ValueParameters_PassStructsByAddressAndScalarsAsValues()
    {
        var plan = CreatePlan("StructKinds");

        Assert.Equal(
            [
                new CpuNativeArgument("global::Native.Pair*", CpuNativeArgumentForm.ValueAddress),
                new CpuNativeArgument(ShuffledShadow + "*", CpuNativeArgumentForm.ShadowAddress),
                new CpuNativeArgument(HolderShadow + "*", CpuNativeArgumentForm.ShadowAddress),
                new CpuNativeArgument(Int32, CpuNativeArgumentForm.Value)
            ],
            plan.Arguments.ToArray()
        );
    }

    // The auto result goes as a pointer to its elements: the declared type, or the shadow of a returned struct whose
    // layouts differ; a void kernel has none
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ScalarResult", Int32 + "*")]
    [InlineData("ConvertedResult", ShuffledShadow + "*")]
    [InlineData("ArrayKinds", null)]
    public void Create_AutoResult_PassesAPointerToItsElements(string kernel, string? expected)
    {
        Assert.Equal(expected, CreatePlan(kernel).ResultTypeName);
    }

    private static CpuNativeArgumentPlan CreatePlan(string kernel)
    {
        var method = s_compilation.GetTypeByMetadataName("Native.Arguments")
            ?.GetMembers(kernel)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.NotNull(method);

        return CpuNativeArgumentPlan.Create(
            KernelHostModel.Create(method, StructMarshalingEmitter.RenderStructs(method))
        );
    }
}
