using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;

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
    [InlineData("data", "float[]?", "Array", "float", "None")]
    [InlineData("data", "float[ ]?", "Array", "float", "None")]
    [InlineData("data", "params float[]?", "Array", "float", "None")]
    [InlineData("data", "float[,]", "MultidimensionalArray", "float", "None")]
    [InlineData("data", "float[ , ]", "MultidimensionalArray", "float", "None")]
    [InlineData("data", "System.Int32[,,]?", "MultidimensionalArray", "System.Int32", "None")]
    [InlineData("n", "ref int", "ByReference", "int", "None")]
    [InlineData("n", "out  MyStruct", "ByReference", "MyStruct", "None")]
    [InlineData("n", "in float", "ByReference", "float", "None")]
    [InlineData("n", "ref readonly int", "ByReference", "int", "None")]
    [InlineData("n", "scoped ref int", "ByReference", "int", "None")]
    [InlineData("n", "scoped out uint", "ByReference", "uint", "None")]
    [InlineData("n", "int", "Value", "int", "None")]
    [InlineData("v", "MyStruct", "Value", "MyStruct", "None")]
    [InlineData("v", "MyNamespace.Nullable<int>", "Value", "MyNamespace.Nullable<int>", "None")]
    [InlineData("v", "MyNamespace.ValueTuple<int, float>", "Value", "MyNamespace.ValueTuple<int, float>", "None")]
    [InlineData("__gsharp_d_data", "int", "Value", "int", "None")]
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
    [InlineData("data", "float[][]", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("data", "float[,][]", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("data", "float[]?[]", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("data", "params float[,]", ModuleKernelParameterTypes.ParamsReason)]
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
    [InlineData("data", "ref float[]", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("data", "out float[]?", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("p", "ref float*", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("s", "ref string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("n", "in int?", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("v", "out (int, float)", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("s", "scoped ref Span<float>", ModuleKernelParameterTypes.RefStructReason)]
    [InlineData("gridDimX", "ref uint", ModuleKernelParameterTypes.LaunchSettingByReferenceReason)]
    [InlineData("stream", "in IntPtr", ModuleKernelParameterTypes.LaunchSettingByReferenceReason)]
    [InlineData("n", "readonly int", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("n", "ref ref int", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("n", "in ref int", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("n", "ref scoped int", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("n", "params ref int[]", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("n", "params int", ModuleKernelParameterTypes.ParamsReason)]
    [InlineData("n", "void", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("n", "float[", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("n", "int x", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("gridDimX", "int", ModuleKernelParameterTypes.DimensionTypeReason)]
    [InlineData("sharedMemBytes", "ulong", ModuleKernelParameterTypes.DimensionTypeReason)]
    [InlineData("stream", "int", ModuleKernelParameterTypes.StreamTypeReason)]
    [InlineData("v", "(int, float)", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("v", "(byte a, double b)", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("v", "ValueTuple<byte, double>", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("v", "global::System.ValueTuple<byte, double>", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("v", "System.DateTime", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("v", "DateTimeOffset", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("data", "(int, float)[]", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("n", "Nullable<float>", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("data", "System.Nullable<int>[]", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("s", "System . String", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("data", "System . String[]", ModuleKernelParameterTypes.ManagedElementReason)]
    [InlineData("s", "Span<float>", ModuleKernelParameterTypes.RefStructReason)]
    [InlineData("s", "scoped Span<float>", ModuleKernelParameterTypes.RefStructReason)]
    [InlineData("s", "scoped ReadOnlySpan<float>", ModuleKernelParameterTypes.RefStructReason)]
    public void Parse_TypeTheLauncherCannotPass_GivesReason(string name, string type, string reason)
    {
        var parsed = ModuleKernelParameterTypes.Parse(name, type);

        Assert.Equal(reason, parsed.Reason);
    }

    // Only an array written nullable passes null to the kernel; the launcher rejects null for another array
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("float[]", false)]
    [InlineData("float[ ] ?", true)]
    [InlineData("params float[]?", true)]
    [InlineData("float[,]", false)]
    [InlineData("float[,]?", true)]
    [InlineData("int", false)]
    public void Parse_ArrayNullability_AllowsNullOnlyWhenWrittenNullable(string type, bool allowsNull)
    {
        var parsed = ModuleKernelParameterTypes.Parse("data", type);

        Assert.Null(parsed.Reason);
        Assert.Equal(allowsNull, parsed.AllowsNull);
    }

    // 'ref readonly' is read as 'in': the kernel's value is not copied back
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("int", RefKind.None)]
    [InlineData("float[]", RefKind.None)]
    [InlineData("ref int", RefKind.Ref)]
    [InlineData("scoped ref int", RefKind.Ref)]
    [InlineData("out int", RefKind.Out)]
    [InlineData("in int", RefKind.In)]
    [InlineData("ref readonly int", RefKind.In)]
    [InlineData("scoped  ref  readonly int", RefKind.In)]
    public void Parse_ParameterModifiers_ReadsRefKind(string type, RefKind refKind)
    {
        var parsed = ModuleKernelParameterTypes.Parse("n", type);

        Assert.Null(parsed.Reason);
        Assert.Equal(refKind, parsed.RefKind);
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
                new ParameterDefinition { Name = "grid", Type = "float[,]?" },
                new ParameterDefinition { Name = "total", Type = "ref int" },
                new ParameterDefinition { Name = "result", Type = "out double" },
                new ParameterDefinition { Name = "seed", Type = "in uint" },
            ],
        };

        var plan = ModuleBindingPlanner.Plan(method);

        Assert.True(plan.CanLaunch);
        ModuleKernelArgument[] expected =
        [
            new ModuleKernelArgument(0, "data", ModuleKernelParameterKind.Array, "float", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument(2, "n", ModuleKernelParameterKind.Value, "int", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument(3, "p", ModuleKernelParameterKind.Pointer, "float*", CopyIn: true, CopyOut: true),
            new ModuleKernelArgument(5, "grid", ModuleKernelParameterKind.MultidimensionalArray, "float", true, true,
                AllowsNull: true),
            new ModuleKernelArgument(6, "total", ModuleKernelParameterKind.ByReference, "int", true, true),
            new ModuleKernelArgument(7, "result", ModuleKernelParameterKind.ByReference, "double", false, true),
            new ModuleKernelArgument(8, "seed", ModuleKernelParameterKind.ByReference, "uint", true, false),
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
            new(0, "input", ModuleKernelParameterKind.Array, "float", CopyIn: true, CopyOut: false),
            new(1, "factor", ModuleKernelParameterKind.Value, "float", CopyIn: true, CopyOut: true),
            new(2, "counts", ModuleKernelParameterKind.Pointer, "int*", CopyIn: true, CopyOut: true),
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

    // A dimension of a struct type with an implicit operator to uint sets the launch like a uint one: the launcher
    // passes it where cuLaunchKernel takes a uint, and C# applies the operator there
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaImportPlanner_DimensionWithUserDefinedConversion_SetsLaunch()
    {
        var plan = PlanCudaImport("Gridded");

        Assert.True(plan.CanLaunch);
        Assert.Equal(new ModuleLaunchConfiguration { GridDimX = "gridDimX", BlockDimX = "blockDimX" }, plan.Launch);
        Assert.Equal(["data"], plan.Arguments.Select(static a => a.Name));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaImportPlanner_FunctionPointersParamsAndInternalLookingName_PassesThem()
    {
        ModuleKernelArgument[] expected =
        [
            new(0, "callback", ModuleKernelParameterKind.Pointer, "delegate* unmanaged<int, void>", true, true),
            new(1, "compute", ModuleKernelParameterKind.Pointer, "delegate*<ref int, float>", true, true),
            new(0, "__gsharp_d_count", ModuleKernelParameterKind.Value, "int", true, true),
            new(1, "values", ModuleKernelParameterKind.Array, "float", true, true),
        ];

        var arguments = PlanCudaImport("Called").Arguments.Concat(PlanCudaImport("Many").Arguments).ToArray();

        Assert.Equal(expected, arguments);
    }

    // LayoutKind.Auto lets the runtime reorder the fields, the ValueTuple<byte, double> of the test case keeps
    // Item1 at offset 8 and Item2 at 0, so the kernel would read other bytes than the C declaration names
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CudaImportPlanner_AutoLayoutValues_ReportsEach()
    {
        var plan = PlanCudaImport("Paired");

        Assert.Equal(
            [
                (0, ModuleKernelParameterTypes.AutoLayoutReason),
                (1, ModuleKernelParameterTypes.AutoLayoutReason),
                (2, ModuleKernelParameterTypes.AutoLayoutReason),
                (3, ModuleKernelParameterTypes.AutoLayoutReason),
            ],
            plan.Problems.Select(static p => (p.ParameterOrdinal, p.Reason)));
    }

    // A value passed by reference is copied in unless it is 'out' and back unless it is 'in' or 'ref readonly'; an
    // array follows [In] / [Out] whatever its rank and nullability
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaImportPlanner_RefKindsAndArrays_StagesThroughDeviceBuffers()
    {
        ModuleKernelArgument[] expected =
        [
            new(0, "total", ModuleKernelParameterKind.ByReference, "int", CopyIn: true, CopyOut: true),
            new(1, "grid", ModuleKernelParameterKind.MultidimensionalArray, "float", CopyIn: true, CopyOut: true),
            new(0, "result", ModuleKernelParameterKind.ByReference, "int", CopyIn: false, CopyOut: true),
            new(1, "seed", ModuleKernelParameterKind.ByReference, "int", CopyIn: true, CopyOut: false),
            new(2, "limit", ModuleKernelParameterKind.ByReference, "int", CopyIn: true, CopyOut: false),
            new(0, "total", ModuleKernelParameterKind.ByReference, "int", CopyIn: true, CopyOut: true),
            new(1, "grid", ModuleKernelParameterKind.MultidimensionalArray, "float", true, false, AllowsNull: true),
            new(2, "values", ModuleKernelParameterKind.Array, "float", false, true, AllowsNull: true),
            new(3, "result", ModuleKernelParameterKind.ByReference, "double", CopyIn: false, CopyOut: true),
            new(4, "seed", ModuleKernelParameterKind.ByReference, "global::TestNamespace.Grid", true, false),
        ];

        var plans = new[] { PlanCudaImport("Counted"), PlanCudaImport("Written"), PlanCudaImport("Packed") };

        Assert.All(plans, static plan => Assert.True(plan.CanLaunch));
        Assert.Equal(expected, plans.SelectMany(static plan => plan.Arguments));
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
