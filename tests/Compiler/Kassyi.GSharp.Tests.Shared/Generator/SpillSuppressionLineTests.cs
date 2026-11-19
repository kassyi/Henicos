using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The CUDA module names the functions whose register spills ptxas reports without a GS0001 warning: the kernels
/// under <c>#pragma warning disable GS0001</c> and the device functions only they reach. The build task compares these
/// names whole with the function of each ptxas line.
/// </summary>
[Category(TestCategory.Compiler)]
public class SpillSuppressionLineTests
{
    private const string Marker = "// gsharp:suppress GS0001";
    private const string AssemblyName = "MegaTestCompilationBase";
    private const string Prefix = "SpillSuppressionTest__Kernels__";

    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Generator/SpillSuppression.cu.cs");

    // One generator run for every test of the CUDA module
    private static readonly Lazy<string> s_cudaModule = new(static () =>
        GenerateModule(RoslynTestUtility.CreateCompilation(s_source), "Cuda", ".g.cu", AssemblyName));

    // The CUDA module of a consumer of a library built in memory, for the tests of the library's functions
    private static readonly Lazy<string> s_libraryModule = new(static () =>
    {
        var library = DeviceLibraryBuilder.Build("SpillSuppressionLibrary", null,
            RoslynTestUtility.ReadTestCase("Generator/SpillSuppressionLibrary.cs"));
        var consumer = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Generator/SpillSuppressionConsumer.cu.cs"), library);
        return GenerateModule(consumer, "Cuda", ".g.cu", "DeviceImportConsumer");
    });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaModule_KernelUnderThePragma_ListsItAndTheFunctionsOnlyItReaches()
    {
        var module = s_cudaModule.Value;

        var names = ReadSuppressionLine(module);

        // Not Shared (UnmarkedKernel reaches it), Countdown (recursive, reached only from UnmarkedKernel), the Via*
        // functions (each reached by a kernel without the pragma), MarkedKernelTwin (a longer name) or UnmarkedKernel
        Assert.Equal(
            [
                "__gsharp_ctor_SpillSuppressionTest__OnlyMarked",
                "__gsharp_fn_" + Prefix + "Leaf",
                "__gsharp_fn_" + Prefix + "Own",
                "__gsharp_fn_SpillSuppressionTest__OnlyMarkedConversion__op_uImplicit",
                "__gsharp_k_" + Prefix + "MarkedKernel",
            ],
            names
        );
        Assert.All(names, name => Assert.Contains(name + "(", module, StringComparison.Ordinal));
    }

    // Each function is reached by MarkedKernel directly and by one kernel without the pragma in one way only (the
    // kernels of the test case): the walk must see that way, or the function would be listed and its spills hidden
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ViaGetter")]
    [InlineData("ViaSetter")]
    [InlineData("ViaIndexer")]
    [InlineData("ViaConversion")]
    [InlineData("ViaMacro")]
    [InlineData("ViaTruthTest")]
    [InlineData("ViaConstructor")]
    [InlineData("ViaInitializer")]
    [InlineData("ViaGenericNew")]
    [InlineData("ViaLocal")]
    [InlineData("ViaLambda")]
    [InlineData("ViaMethodGroup")]
    [InlineData("ViaDispose")]
    [InlineData("ViaBuilder")]
    [InlineData("ViaPinnable")]
    [InlineData("ViaNested")]
    [InlineData("ViaUsingDeclaration")]
    [InlineData("ViaUsingExpression")]
    [InlineData("ViaFiniteGrowth")]
    [InlineData("ViaIndirectGrowth")]
    public void CudaModule_FunctionAnUnmarkedKernelReachesOnlyThrough_IsNotListed(string function)
    {
        var module = s_cudaModule.Value;
        var name = "__gsharp_fn_" + Prefix + function;

        Assert.Contains("int " + name + "(int", module, StringComparison.Ordinal);
        Assert.DoesNotContain(name, ReadSuppressionLine(module));
    }

    // The library's G calls F; the consumer's call graph sees only G, so F would be listed (hidden) if the records of
    // the library were not followed. Only, reached from the marked kernel through the library's Relay, is listed
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaModule_FunctionReachedThroughAnotherAssembly_FollowsTheLibraryRecords()
    {
        var module = s_libraryModule.Value;

        var names = ReadSuppressionLine(module);

        Assert.Equal(
            [
                "__gsharp_fn_SpillSuppressionLibrary__Lib__Only",
                "__gsharp_fn_SpillSuppressionLibrary__Lib__Relay",
                "__gsharp_k_SpillSuppressionConsumer__Kernels__MarkedKernel",
            ],
            names
        );
        Assert.All(names, name => Assert.Contains(name + "(", module, StringComparison.Ordinal));
    }

    // Each library function is called by the marked kernel directly and reached by one unmarked kernel in one way only:
    // a library function (G -> F), a chain of two steps (G2 -> H -> F2), a library struct's getter, a library function
    // that uses a library struct's getter and a library struct's constructor
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("F")]
    [InlineData("F2")]
    [InlineData("FGetter")]
    [InlineData("FViaFn")]
    [InlineData("FCtor")]
    public void CudaModule_LibraryFunctionAnUnmarkedKernelReachesOnlyThrough_IsNotListed(string function)
    {
        var module = s_libraryModule.Value;
        var name = "__gsharp_fn_SpillSuppressionLibrary__Lib__" + function;

        Assert.Contains("int " + name + "(int", module, StringComparison.Ordinal);
        Assert.DoesNotContain(name, ReadSuppressionLine(module));
    }

    // Each kernel reaches a struct that constructs itself, or another that constructs it back, with a type argument
    // that holds its own type parameter: every step is a new construction, so the walk has to stop on its own
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("DeepKernel")]
    [InlineData("AlternatingKernel")]
    [InlineData("SwapKernel")]
    [InlineData("MutualKernel")]
    [InlineData("NewTypeParameterKernel")]
    public async Task GetReachedFunctionNames_PolymorphicRecursionOfAGenericStruct_Ends(string kernelName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Generator/SpillSuppressionPolymorphicRecursion.cu.txt"));
        var declaration = compilation.SyntaxTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == kernelName);
        var kernel = Assert.IsAssignableFrom<IMethodSymbol>(
            compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration));

        var names = await Task.Run(() => DeviceFunctionReach.GetReachedFunctionNames(kernel, declaration, compilation))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Contains("__gsharp_fn_PolymorphicRecursion__Kernels__Step", names);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CppModule_KernelUnderThePragma_HasNoSuppressionLine()
    {
        // ptxas reports the spills; the C++ dialect has no GS0001
        var module = GenerateModule(RoslynTestUtility.CreateCompilation(s_source), "Cpu", ".g.cpp", AssemblyName);

        Assert.DoesNotContain(Marker, module, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_FunctionSharedWithAKernelWithoutThePragma_LeavesItReported()
    {
        KernelSpillScope[] kernels =
        [
            new("__gsharp_k_A", true, Names("__gsharp_fn_Own", "__gsharp_fn_Shared")),
            new("__gsharp_k_B", false, Names("__gsharp_fn_Shared")),
            new("__gsharp_k_C", false, Names()),
        ];

        Assert.Equal(["__gsharp_fn_Own", "__gsharp_k_A"], SpillSuppressionPlan.Create(kernels).ToArray());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_NoKernelUnderThePragma_ListsNothingAndWritesNoLine()
    {
        KernelSpillScope[] kernels = [new("__gsharp_k_B", false, Names("__gsharp_fn_Shared"))];

        var names = SpillSuppressionPlan.Create(kernels);

        Assert.Empty(names);
        Assert.Equal("", SpillSuppressionPlan.FormatLine(names));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_KernelWithoutAScope_IsSkipped()
    {
        // A kernel whose transpilation failed has the default scope: no name, no reached functions
        KernelSpillScope[] kernels = [default, new("__gsharp_k_A", true, Names("__gsharp_fn_X"))];

        Assert.Equal(["__gsharp_fn_X", "__gsharp_k_A"], SpillSuppressionPlan.Create(kernels).ToArray());
    }

    // The names of the one suppression line of the module
    private static List<string> ReadSuppressionLine(string module)
    {
        var line = Assert.Single(Lines(module), static l => l.StartsWith(Marker, StringComparison.Ordinal));
        return Words(line.AsSpan(Marker.Length));
    }

    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static List<string> Words(ReadOnlySpan<char> text)
    {
        var words = new List<string>();
        foreach (var range in text.Split(' '))
        {
            if (text[range].Length > 0)
            {
                words.Add(text[range].ToString());
            }
        }

        return words;
    }

    private static EquatableArray<string> Names(params string[] names) => names.ToImmutableArray().AsEquatableArray();

    private static string GenerateModule(Compilation compilation, string dialect, string extension,
        string assemblyName)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var options = new Dictionary<string, string>
            {
                { "build_property.IntermediateOutputPath", "." },
                { "build_property.MSBuildProjectDirectory", directory },
            };
            GeneratorTestRunner.RunGenerator(compilation, dialect, options);
            return File.ReadAllText(Path.Combine(directory, $"Kassyi.GSharp.Kernels_{assemblyName}{extension}"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
