using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies the names of the instances of generic kernels and device functions: each set of type arguments gets its
/// own C++ name, the closed copy of a declaration is named like a call of the constructed method, and the names decode
/// back.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericInstanceNamesTests
{
    private static readonly string s_source =
        RoslynTestUtility.ReadTestCase("Transpiler/GenericInstanceNamesSource.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetFunctionInstanceName_ConstructedMethod_EncodesTheTypeArgumentsAndDecodesBack()
    {
        var compilation = CreateCompilation();
        var instance = GenericInstance.Of(Method(compilation, "Functions.G", "Scale").Construct(Int(compilation)));

        var name = GenericInstanceNames.GetFunctionName(instance);

        Assert.Equal("__gsharp_fn_Functions__G__Scale_g1_bSystem__Int32_e", name);
        Assert.True(GSharpNameEncoding.TryDecode(name, out var display));
        Assert.Equal("Functions.G.Scale<System.Int32>", display);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetFunctionInstanceName_DifferentTypeArguments_GiveDistinctNames()
    {
        var compilation = CreateCompilation();
        var scale = Method(compilation, "Functions.G", "Scale");
        var intType = Int(compilation);
        var generic = Type(compilation, "Repro.Generic`1");
        ITypeSymbol[] arguments =
        [
            intType,
            compilation.GetSpecialType(SpecialType.System_Single),
            compilation.CreateArrayTypeSymbol(intType),
            generic.Construct(intType),
            generic.Construct(generic.Construct(intType)),
            Type(compilation, "GlobalType"),
        ];

        var names = arguments
            .Select(argument => GenericInstanceNames.GetFunctionName(GenericInstance.Of(scale.Construct(argument))))
            .Append(GlobalCppNames.GetFunctionName(scale))
            .ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("__gsharp_fn_Functions__G__Scale_a1", names[^1]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetFunctionInstanceName_TwoTypeParameters_KeepsTheirOrder()
    {
        var compilation = CreateCompilation();
        var convert = Method(compilation, "Functions.G", "Convert");
        var intType = Int(compilation);
        var floatType = compilation.GetSpecialType(SpecialType.System_Single);

        var intToFloat = GenericInstanceNames.GetFunctionName(
            GenericInstance.Of(convert.Construct(intType, floatType)));
        var floatToInt = GenericInstanceNames.GetFunctionName(
            GenericInstance.Of(convert.Construct(floatType, intType)));

        Assert.Equal("__gsharp_fn_Functions__G__Convert_g2_bSystem__Int32_bSystem__Single_e", intToFloat);
        Assert.Equal("__gsharp_fn_Functions__G__Convert_g2_bSystem__Single_bSystem__Int32_e", floatToInt);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetFunctionInstanceName_OverloadedGenericMethod_CarriesTheOrdinalOfTheDeclaration()
    {
        var compilation = CreateCompilation();
        var pickTwo = Methods(compilation, "Functions.G", "Pick")[1];

        var name = GenericInstanceNames.GetFunctionName(GenericInstance.Of(pickTwo.Construct(Int(compilation))));

        Assert.Equal("__gsharp_fn_Functions__G__Pick_g1_bSystem__Int32_e_o1", name);
        Assert.True(GSharpNameEncoding.TryDecode(name, out var display));
        Assert.Equal("Functions.G.Pick<System.Int32>#1", display);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetKernelInstanceName_MemberOfConstructedType_EncodesEveryLevel()
    {
        var compilation = CreateCompilation();
        var kernels = Type(compilation, "Functions.Outer`1").Construct(Int(compilation)).GetTypeMembers("Kernels")[0];
        var map = kernels.GetMembers("Map").OfType<IMethodSymbol>().Single()
            .Construct(compilation.GetSpecialType(SpecialType.System_Single));
        var run = kernels.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var mapInstance = GenericInstance.Of(map);
        var runName = GenericInstanceNames.GetKernelName(GenericInstance.Of(run));
        var mapName = GenericInstanceNames.GetKernelName(mapInstance);

        Assert.Equal("__gsharp_k_Functions__Outer_g1_bSystem__Int32_e__Kernels__Run", runName);
        Assert.Equal("__gsharp_k_Functions__Outer_g1_bSystem__Int32_e__Kernels__Map_g1_bSystem__Single_e", mapName);
        Assert.Equal(new[] { SpecialType.System_Int32, SpecialType.System_Single },
            mapInstance.TypeArguments.Select(t => t.SpecialType));
        Assert.True(GSharpNameEncoding.TryDecode(mapName, out var display));
        Assert.Equal("Functions.Outer<System.Int32>.Kernels.Map<System.Single>", display);
    }

    // The closed copy is an ordinary method; registered, every name derived from it is the name of the instance.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetFunctionName_RegisteredCopy_IsTheNameOfTheInstance()
    {
        var compilation = CreateCompilation();
        var instance = GenericInstance.Of(Methods(compilation, "Functions.G", "Pick")[1].Construct(Int(compilation)));
        var copy = Method(compilation, "Functions.G", "PickCopy");

        GenericInstanceTable.Register(copy, instance);

        Assert.Equal(GenericInstanceNames.GetFunctionName(instance), GlobalCppNames.GetFunctionName(copy));
        Assert.Equal(GenericInstanceNames.GetInstancePath(instance), GlobalCppNames.GetFunctionPath(copy));
        Assert.Equal("__gsharp_fn_Functions__G__Pick_g1_bSystem__Int32_e_o1", GlobalCppNames.GetFunctionName(copy));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetKernelName_RegisteredCopy_IsTheNameOfTheInstance()
    {
        var compilation = CreateCompilation();
        var instance = GenericInstance.Of(Method(compilation, "Functions.G", "Scale").Construct(Int(compilation)));
        var copy = Method(compilation, "Functions.G", "ScaleCopy");

        GenericInstanceTable.Register(copy, instance);

        Assert.Equal(GenericInstanceNames.GetKernelName(instance), GlobalCppNames.GetKernelName(copy));
        Assert.Equal("__gsharp_launch_Functions__G__Scale_g1_bSystem__Int32_e",
            GlobalCppNames.GetKernelDerivedName("__gsharp_launch_", copy));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetFunctionName_UnregisteredMethod_KeepsItsOwnName()
    {
        var compilation = CreateCompilation();

        Assert.Equal("__gsharp_fn_Functions__G__PickCopy",
            GlobalCppNames.GetFunctionName(Method(compilation, "Functions.G", "PickCopy")));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Register_CopyOfAnotherInstance_Throws()
    {
        var compilation = CreateCompilation();
        var scale = Method(compilation, "Functions.G", "Scale");
        var copy = Method(compilation, "Functions.G", "ScaleCopy");
        GenericInstanceTable.Register(copy, GenericInstance.Of(scale.Construct(Int(compilation))));

        GenericInstanceTable.Register(copy, GenericInstance.Of(scale.Construct(Int(compilation))));
        Assert.Throws<InvalidOperationException>(() => GenericInstanceTable.Register(copy,
            GenericInstance.Of(scale.Construct(compilation.GetSpecialType(SpecialType.System_Single)))));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Of_OpenOrNonGenericMethod_Throws()
    {
        var compilation = CreateCompilation();
        var scale = Method(compilation, "Functions.G", "Scale");
        var otherParameter = Method(compilation, "Functions.G", "Open").TypeParameters[0];
        var generic = Type(compilation, "Repro.Generic`1");

        Assert.Throws<ArgumentException>(() => GenericInstance.Of(scale));
        Assert.Throws<ArgumentException>(() => GenericInstance.Of(scale.Construct(otherParameter)));
        Assert.Throws<ArgumentException>(() => GenericInstance.Of(scale.Construct(generic.Construct(otherParameter))));
        Assert.Throws<ArgumentException>(() =>
            GenericInstance.Of(scale.Construct(compilation.CreateArrayTypeSymbol(otherParameter))));
        Assert.Throws<ArgumentException>(() => GenericInstance.Of(Method(compilation, "Functions.G", "PickCopy")));
        Assert.Throws<ArgumentException>(() => GenericInstance.Of(
            Type(compilation, "Functions.Outer`1").GetTypeMembers("Kernels")[0].GetMembers("Run")
                .OfType<IMethodSymbol>().Single()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetCopyName_Instance_CarriesEveryTypeArgumentAndDecodesBack()
    {
        var compilation = CreateCompilation();
        var pickTwo = Methods(compilation, "Functions.G", "Pick")[1];
        var scale = Method(compilation, "Functions.G", "Scale");

        var pickName = GenericInstanceNames.GetCopyName(GenericInstance.Of(pickTwo.Construct(Int(compilation))));
        var globalName = GenericInstanceNames.GetCopyName(
            GenericInstance.Of(scale.Construct(Type(compilation, "GlobalType"))));

        Assert.Equal("__gsharp_gi_Pick_g1_bSystem__Int32_e_o1", pickName);
        Assert.Equal("__gsharp_gi_Scale_g1_bGlobalType_e", globalName);
        Assert.True(GSharpNameEncoding.TryDecode(globalName, out var display));
        Assert.Equal("Scale<GlobalType>", display);
    }

    // The copy of a member of Outer<T> is declared in Outer<T> itself, so the arguments of Outer tell the copies apart.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetCopyName_MembersOfDifferentConstructions_GiveDistinctNames()
    {
        var compilation = CreateCompilation();
        var outer = Type(compilation, "Functions.Outer`1");

        string CopyName(SpecialType type) => GenericInstanceNames.GetCopyName(GenericInstance.Of(
            outer.Construct(compilation.GetSpecialType(type)).GetTypeMembers("Kernels")[0].GetMembers("Run")
                .OfType<IMethodSymbol>().Single()));

        Assert.Equal("__gsharp_gi_Run_g1_bSystem__Int32_e", CopyName(SpecialType.System_Int32));
        Assert.Equal("__gsharp_gi_Run_g1_bSystem__Single_e", CopyName(SpecialType.System_Single));
    }

    private static INamedTypeSymbol Int(Compilation compilation) =>
        compilation.GetSpecialType(SpecialType.System_Int32);

    private static IMethodSymbol Method(Compilation compilation, string type, string name) =>
        Methods(compilation, type, name).Single();

    // In declaration order.
    private static IMethodSymbol[] Methods(Compilation compilation, string type, string name) =>
        Type(compilation, type).GetMembers(name).OfType<IMethodSymbol>()
            .OrderBy(m => m.Locations[0].SourceSpan.Start)
            .ToArray();

    private static INamedTypeSymbol Type(Compilation compilation, string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException($"Test source does not declare '{metadataName}'.");

    private static CSharpCompilation CreateCompilation() =>
        RoslynTestUtility.CreateCompilation(s_source)
            .WithAssemblyName("GenericInstanceNamesTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
