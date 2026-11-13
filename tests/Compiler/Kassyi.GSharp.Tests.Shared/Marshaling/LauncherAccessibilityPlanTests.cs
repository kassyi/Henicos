using System.Collections.Immutable;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the accessibilities that the host model plans for the generated members of a kernel class (#1284): the
/// launchers, the marshaling class <c>AotContexts</c>, the pointer launchers that name it, and the managed state
/// machine with its context, without reading generated text.
/// </summary>
[Category(TestCategory.Compiler)]
public class LauncherAccessibilityPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/LauncherAccessibilitySource.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("PrivK", Accessibility.Private)]
    [InlineData("RefK", Accessibility.Public)]
    [InlineData("ShadowK", Accessibility.Public)]
    public void LauncherAccessibility_ParameterTypes_FollowsThem(string kernel, Accessibility expected)
    {
        var host = KernelHostModel.Create(GetKernel("Access.Mixed", kernel), default);

        Assert.Equal(expected, host.LauncherAccessibility);
    }

    // The AotContexts class takes the accessibility of the structs it marshals
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("PrivK", Accessibility.Private)]
    [InlineData("ShadowK", Accessibility.Public)]
    public void RenderStructs_StructAccessibility_FollowsTheStructAndItsMembers(string kernel, Accessibility expected)
    {
        var structs = StructMarshalingEmitter.RenderStructs(GetKernel("Access.Mixed", kernel));

        Assert.Equal([expected], structs.Select(static s => s.Accessibility));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Access.Mixed", Accessibility.Private)]
    [InlineData("Access.Open", Accessibility.Public)]
    public void AotContextsAccessibility_StructsOfTheClass_TakesTheNarrowest(string className, Accessibility expected)
    {
        Assert.Equal(expected, CreateModel(className).AotContextsAccessibility);
    }

    // One case per way a pointer launcher passes a parameter: an overloaded kernel's typed launcher takes a ref struct
    // as GpuBuffer<AotContexts.X>, the untyped launcher a struct by value as AotContexts.X, an array of a blittable
    // struct as GpuBuffer<AotContexts.X>; an array of int and a scalar name no AotContexts type. A launcher that
    // names AotContexts is narrowed to it; the untyped launcher of an overload is private
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Access.Mixed", "RefK", Accessibility.Public, Accessibility.Private, Accessibility.Private)]
    [InlineData("Access.Mixed", "ShadowK", Accessibility.Public, Accessibility.Private, Accessibility.Private)]
    [InlineData("Access.Mixed", "ArrayOfK", Accessibility.Public, Accessibility.Private, Accessibility.Private)]
    [InlineData("Access.Mixed", "ArrayK", Accessibility.Public, Accessibility.Public, Accessibility.Public)]
    [InlineData("Access.Mixed", "PrivK", Accessibility.Private, Accessibility.Private, Accessibility.Private)]
    [InlineData("Access.Open", "RefK", Accessibility.Public, Accessibility.Private, Accessibility.Public)]
    [InlineData("Access.Open", "ShadowK", Accessibility.Public, Accessibility.Public, Accessibility.Public)]
    public void GetLauncherAccessibilities_PointerArgumentKinds_NarrowTheLaunchersThatNameAotContexts(
        string className,
        string kernel,
        Accessibility launcher,
        Accessibility pointer,
        Accessibility typedPointer)
    {
        var model = CreateModel(className);
        var emission = model.Kernels.First(e => e.Host.Name == kernel);

        Assert.Equal(new LauncherAccessibilities(launcher, pointer, typedPointer),
            model.GetLauncherAccessibilities(emission));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("RefK", "r", true)]
    [InlineData("ShadowK", "s", true)]
    [InlineData("ArrayOfK", "a", true)]
    [InlineData("ArrayK", "a", false)]
    [InlineData("ArrayK", "n", false)]
    public void TypedPointerNamesShadowStruct_Parameter_TellsWhetherTheTypedLauncherNamesAotContexts(
        string kernel,
        string parameter,
        bool expected)
    {
        var shape = KernelParameterShape.Classify(
            GetKernel("Access.Mixed", kernel).Parameters.First(p => p.Name == parameter));

        Assert.Equal(expected, KernelClassModel.TypedPointerNamesShadowStruct(shape));
    }

    // The context class follows its field types, the state machine also the kernel's signature
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PublicKernel", false, Accessibility.Public, Accessibility.Public)]
    [InlineData("PublicKernel", true, Accessibility.Private, Accessibility.Private)]
    [InlineData("PrivateParameterKernel", false, Accessibility.Public, Accessibility.Private)]
    public void KernelFrameAccessibility_FieldAndSignatureTypes_TakesTheWidestThatCompiles(
        string kernel,
        bool holdsPrivateLocal,
        Accessibility context,
        Accessibility stateMachine)
    {
        ImmutableArray<ITypeSymbol> fields = holdsPrivateLocal
            ? [GetType("Access.Frames+Local")]
            : [];

        Assert.Equal(new KernelFrameAccessibility(context, stateMachine),
            KernelFrameAccessibility.Create(GetKernel("Access.Frames", kernel), fields));
    }

    // The class model of the kernels of one class, as the generator builds it from the transpiled kernels
    private static KernelClassModel CreateModel(string className)
    {
        var type = GetType(className);
        var hosts = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static m => m.GetAttributes().Any(static a => a.AttributeClass?.Name == "GpuKernelAttribute"))
            .Select(static k => KernelHostModel.Create(k, StructMarshalingEmitter.RenderStructs(k)))
            .ToList();
        var emissions = hosts
            .Select((host, i) => new KernelEmission(
                host,
                host.Name,
                KernelClassModel.IsOverloaded(hosts, i),
                IsIndistinguishable: false,
                CudaSource: "",
                CppSource: "",
                ManagedCSharpSource: "",
                InternalError: ""))
            .ToImmutableArray();
        var structs = hosts
            .SelectMany(static host => host.Structs)
            .GroupBy(static s => s.AotStructPath)
            .Select(static group => group.First())
            .ToImmutableArray();
        return new KernelClassModel(
            HostClassHeader.Create(type, s_compilation),
            emissions.AsEquatableArray(),
            structs.AsEquatableArray());
    }

    private static IMethodSymbol GetKernel(string className, string name) =>
        GetType(className).GetMembers(name).OfType<IMethodSymbol>().First();

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException($"Type '{metadataName}' not found.");
}
