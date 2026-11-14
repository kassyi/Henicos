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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CudaModule_KernelUnderThePragma_ListsItAndTheFunctionsOnlyItReaches()
    {
        var module = GenerateModule("Cuda", ".g.cu");

        var line = Assert.Single(Lines(module), static l => l.StartsWith(Marker, StringComparison.Ordinal));
        var names = Words(line.AsSpan(Marker.Length));

        // Not Shared (UnmarkedKernel reaches it), Countdown (recursive, reached only from UnmarkedKernel),
        // MarkedKernelTwin (a longer name) or UnmarkedKernel
        Assert.Equal(
            [
                "__gsharp_fn_" + Prefix + "Leaf",
                "__gsharp_fn_" + Prefix + "Own",
                "__gsharp_k_" + Prefix + "MarkedKernel",
            ],
            names
        );
        Assert.All(names, name => Assert.Contains(name + "(", module, StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CppModule_KernelUnderThePragma_HasNoSuppressionLine()
    {
        // ptxas reports the spills; the C++ dialect has no GS0001
        var module = GenerateModule("Cpu", ".g.cpp");

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

    private static string GenerateModule(string dialect, string extension)
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
            GeneratorTestRunner.RunGenerator(s_source, dialect, options);
            return File.ReadAllText(Path.Combine(directory, $"Kassyi.GSharp.Kernels_{AssemblyName}{extension}"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
