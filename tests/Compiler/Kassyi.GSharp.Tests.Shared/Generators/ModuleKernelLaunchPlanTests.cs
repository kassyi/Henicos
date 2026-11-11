using Kassyi.GSharp.Compiler.NativeApi;

#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// The decisions of the module launcher planners, without generated text: how a YAML parameter type is read
/// (<see cref="ModuleKernelParameterTypes.Parse"/>, shared by validation and the launcher) and how the parameters of a
/// YAML binding and of a <c>[CudaImport]</c> method become kernel arguments and launch settings.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ModuleKernelLaunchPlanTests
{
    // An array is an array however the rank specifier is spaced (#1503), and the element type is passed on as written
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("data", "float[]", "Array", "float", "None")]
    [InlineData("data", "float[ ]", "Array", "float", "None")]
    [InlineData("data", "float [ ]", "Array", "float", "None")]
    [InlineData("data", " System.Single [] ", "Array", "System.Single", "None")]
    [InlineData("data", "global::System.Single[]", "Array", "global::System.Single", "None")]
    [InlineData("data", "params int[]", "Array", "int", "None")]
    [InlineData("n", "int", "Value", "int", "None")]
    [InlineData("v", "MyStruct", "Value", "MyStruct", "None")]
    [InlineData("v", "(int, float)", "Value", "(int, float)", "None")]
    [InlineData("callback", "Action", "Value", "Action", "None")]
    [InlineData("p", "float *", "Pointer", "float*", "None")]
    [InlineData("f", "delegate* unmanaged<int, void>", "Pointer", "delegate* unmanaged<int, void>", "None")]
    [InlineData("gridDimX", "uint", "Value", "uint", "GridDimX")]
    [InlineData("blockDimZ", "System.UInt16", "Value", "System.UInt16", "BlockDimZ")]
    [InlineData("sharedMemBytes", "byte", "Value", "byte", "SharedMemBytes")]
    [InlineData("stream", "IntPtr", "Value", "IntPtr", "Stream")]
    [InlineData("stream", "nint", "Value", "nint", "Stream")]
    [InlineData("stream", "global::System.IntPtr", "Value", "global::System.IntPtr", "Stream")]
    public void Parse_TypeTheLauncherCanPass_ReadsKindAndPassedType(
        string name,
        string type,
        string kind,
        string passedType,
        string launchSetting)
    {
        var parsed = ModuleKernelParameterTypes.Parse(name, type);

        Assert.Null(parsed.Reason);
        Assert.Equal(kind, parsed.Kind.ToString());
        Assert.Equal(passedType, parsed.ElementTypeName ?? parsed.TypeName);
        Assert.Equal(launchSetting, parsed.LaunchSetting.ToString());
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("data", "float[ ]?", ModuleKernelParameterTypes.NullableArrayReason)]
    [InlineData("data", "float[,]", ModuleKernelParameterTypes.MultidimensionalArrayReason)]
    [InlineData("data", "float[ , ]", ModuleKernelParameterTypes.MultidimensionalArrayReason)]
    [InlineData("data", "float[][]", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("data", "float*[]", ModuleKernelParameterTypes.PointerElementReason)]
    [InlineData("data", "string[]", ModuleKernelParameterTypes.ManagedElementReason)]
    [InlineData("data", "int?[]", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("n", "int?", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("s", "string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("s", "string?", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("o", "object", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("o", "global::System.Object", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("o", "dynamic", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("a", "System.Array", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("n", "ref int", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("n", "out int", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("n", "in int", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("n", "ref readonly int", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("n", "scoped ref int", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("n", "params int", ModuleKernelParameterTypes.ParamsReason)]
    [InlineData("n", "void", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("n", "float[", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("n", "int x", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("gridDimX", "int", ModuleKernelParameterTypes.DimensionTypeReason)]
    [InlineData("sharedMemBytes", "ulong", ModuleKernelParameterTypes.DimensionTypeReason)]
    [InlineData("stream", "int", ModuleKernelParameterTypes.StreamTypeReason)]
    [InlineData("__gsharp_d_data", "int", ModuleKernelParameterTypes.ReservedNameReason)]
    public void Parse_TypeTheLauncherCannotPass_GivesReason(string name, string type, string reason)
    {
        var parsed = ModuleKernelParameterTypes.Parse(name, type);

        Assert.Equal(reason, parsed.Reason);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleBindingPlanner_MixedParameters_SplitsKernelArgumentsFromLaunchSettings()
    {
        var method = new ApiMethodDefinition
        {
            Name = "Run",
            Parameters =
            [
                new ParameterDefinition { Name = "data", Type = "float [ ]" },
                new ParameterDefinition { Name = "gridDimX", Type = "uint" },
                new ParameterDefinition { Name = "n", Type = "int" },
                new ParameterDefinition { Name = "p", Type = "float*" },
                new ParameterDefinition { Name = "stream", Type = "IntPtr" },
            ],
        };

        var plan = ModuleBindingPlanner.Plan(method);

        Assert.True(plan.CanLaunch);
        ModuleKernelArgument[] expected =
        [
            new ModuleKernelArgument("data", ModuleKernelParameterKind.Array, "float", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument("n", ModuleKernelParameterKind.Value, "int", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument("p", ModuleKernelParameterKind.Pointer, "float*", CopyIn: true, CopyOut: true),
        ];
        Assert.Equal(expected, plan.Arguments.AsImmutableArray().ToArray());
        Assert.Equal(new ModuleLaunchConfiguration { GridDimX = "gridDimX", Stream = "stream" }, plan.Launch);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaImportPlanner_PassableParameters_ClassifiesFromSymbols()
    {
        var plan = PlanCudaImport("Passable");

        Assert.True(plan.CanLaunch);
        ModuleKernelArgument[] expected =
        [
            new ModuleKernelArgument("input", ModuleKernelParameterKind.Array, "float", CopyIn: true, CopyOut: false),
            new ModuleKernelArgument("factor", ModuleKernelParameterKind.Value, "float", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument("counts", ModuleKernelParameterKind.Pointer, "int*", CopyIn: true, CopyOut: true),
        ];
        Assert.Equal(expected, plan.Arguments.AsImmutableArray().ToArray());
        Assert.Equal(new ModuleLaunchConfiguration { GridDimX = "gridDimX", Stream = "stream" }, plan.Launch);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Text", 0, ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("Callback", 0, ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("Rows", 0, ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("Streamed", 1, ModuleKernelParameterTypes.StreamTypeReason)]
    [InlineData("Optional", 0, ModuleKernelParameterTypes.NullableValueReason)]
    public void CudaImportPlanner_ParameterTheLauncherCannotPass_ReportsProblemAndNoArgument(
        string method,
        int ordinal,
        string reason)
    {
        var plan = PlanCudaImport(method);

        Assert.False(plan.CanLaunch);
        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ordinal, problem.ParameterOrdinal);
        Assert.Equal(reason, problem.Reason);
        Assert.DoesNotContain(plan.Arguments, a => a.Name == problem.ParameterName);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CudaImportPlanner_RefAndMultidimensionalParameters_ReportsBoth()
    {
        var plan = PlanCudaImport("Counted");

        Assert.Equal(
            [
                (0, ModuleKernelParameterTypes.ByReferenceReason),
                (1, ModuleKernelParameterTypes.MultidimensionalArrayReason),
            ],
            plan.Problems.Select(static p => (p.ParameterOrdinal, p.Reason)));
    }

    private static ModuleKernelLaunchPlan PlanCudaImport(string methodName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportParameterDiagnostics.cs"));
        var type = compilation.GetTypeByMetadataName("TestNamespace.ImportedKernels")
                   ?? throw new InvalidOperationException("The test case declares ImportedKernels.");
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        return CudaImportPlanner.Plan(method, compilation);
    }
}
