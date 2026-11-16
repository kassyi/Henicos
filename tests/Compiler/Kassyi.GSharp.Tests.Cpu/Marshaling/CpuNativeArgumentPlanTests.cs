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
    private const string PairShadow = "global::Native.Arguments.AotContexts.Pair";
    private const string Boolean = "global::System.Boolean";
    private const string Char = "global::System.Char";

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

    // The pointer launcher takes the caller's proxies: an array-like goes as the proxy itself, a Nullable<array-like>
    // as the address of a copy of the caller's GSharpArrayProxy?
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateForPointerLauncher_ArrayLikeParameters_PassTheCallersProxies()
    {
        var plan = CreatePointerPlan("ArrayKinds");

        Assert.Equal(
            [
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ProxyCopy),
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ProxyCopy),
                new CpuNativeArgument(ArrayProxy, CpuNativeArgumentForm.ProxyCopy),
                new CpuNativeArgument(NullableArrayProxy, CpuNativeArgumentForm.NullableProxyAddress)
            ],
            plan.Arguments.Arguments.ToArray()
        );
    }

    // A ref / out parameter is the caller's IntPtr, cast to the pointer type of the native launcher: the declared type,
    // or the shadow of a struct whose layouts differ, as Launch{Kernel} passes it
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateForPointerLauncher_ReferenceParameters_CastTheCallersAddresses()
    {
        var plan = CreatePointerPlan("ReferenceKinds");

        Assert.Equal(
            [
                new CpuNativeArgument(Int32 + "*", CpuNativeArgumentForm.PointerCast),
                new CpuNativeArgument(Int32 + "*", CpuNativeArgumentForm.PointerCast),
                new CpuNativeArgument("global::Native.Pair*", CpuNativeArgumentForm.PointerCast),
                new CpuNativeArgument(ShuffledShadow + "*", CpuNativeArgumentForm.PointerCast)
            ],
            plan.Arguments.Arguments.ToArray()
        );
    }

    // The pointer launcher takes every struct by value as its shadow struct, also one whose layouts agree, and passes
    // the shadow parameter by address
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateForPointerLauncher_StructValues_PassTheShadowByAddress()
    {
        var plan = CreatePointerPlan("StructKinds");

        Assert.Equal(
            [
                new CpuNativeArgument(PairShadow + "*", CpuNativeArgumentForm.ValueAddress),
                new CpuNativeArgument(ShuffledShadow + "*", CpuNativeArgumentForm.ValueAddress),
                new CpuNativeArgument(HolderShadow + "*", CpuNativeArgumentForm.ValueAddress),
                new CpuNativeArgument(Int32, CpuNativeArgumentForm.Value)
            ],
            plan.Arguments.Arguments.ToArray()
        );
    }

    // An unmanaged function pointer marshals bool as a 4-byte BOOL and char as one ANSI byte, so the pointer launcher
    // passes them in the widths of the C++ bool and char16_t; the delegate of Launch{Kernel} keeps them (it is declared
    // with CharSet.Unicode)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CreateForPointerLauncher_BoolAndChar_PassTheNativeWidths()
    {
        Assert.Equal(
            [
                new CpuNativeArgument("byte", CpuNativeArgumentForm.BooleanByte),
                new CpuNativeArgument("ushort", CpuNativeArgumentForm.CharCode),
                new CpuNativeArgument(Int32, CpuNativeArgumentForm.Value)
            ],
            CreatePointerPlan("ScalarKinds").Arguments.Arguments.ToArray()
        );
        Assert.Equal(
            [
                new CpuNativeArgument(Boolean, CpuNativeArgumentForm.Value),
                new CpuNativeArgument(Char, CpuNativeArgumentForm.Value),
                new CpuNativeArgument(Int32, CpuNativeArgumentForm.Value)
            ],
            CreatePlan("ScalarKinds").Arguments.ToArray()
        );
    }

    // The auto result is a host array in both launchers, so the pointer launcher passes it as Launch{Kernel} does
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ScalarResult", Int32 + "*")]
    [InlineData("ConvertedResult", ShuffledShadow + "*")]
    [InlineData("ArrayKinds", null)]
    public void CreateForPointerLauncher_AutoResult_PassesAPointerToItsElements(string kernel, string? expected)
    {
        Assert.Equal(expected, CreatePointerPlan(kernel).Arguments.ResultTypeName);
    }

    // The managed CPU path does not run a pointer launcher yet: the branch throws, naming the launcher the caller
    // called and the launcher that runs there
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CreateForPointerLauncher_ManagedExecution_IsNotSupported()
    {
        var message = CreatePointerPlan("ArrayKinds").ManagedExecutionMessage;

        Assert.StartsWith("LaunchArrayKindsPtr runs only on the native CPU path", message, StringComparison.Ordinal);
        Assert.Contains("LaunchArrayKinds,", message, StringComparison.Ordinal);
    }

    private static CpuNativeArgumentPlan CreatePlan(string kernel) => CpuNativeArgumentPlan.Create(CreateModel(kernel));

    private static CpuPointerLaunchPlan CreatePointerPlan(string kernel) => CpuPointerLaunchPlan.Create(CreateModel(kernel));

    private static KernelHostModel CreateModel(string kernel)
    {
        var method = s_compilation.GetTypeByMetadataName("Native.Arguments")
            ?.GetMembers(kernel)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.NotNull(method);

        return KernelHostModel.Create(method, StructMarshalingEmitter.RenderStructs(method));
    }
}
