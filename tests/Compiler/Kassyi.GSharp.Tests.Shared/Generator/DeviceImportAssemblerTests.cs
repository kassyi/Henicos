#nullable enable
using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The text that <see cref="DeviceImportAssembler"/> places in a consumer's native module, from records written by hand
/// (HandWrittenImports.cs) whose texts are markers: what is reached in each dialect, its order, its includes and the
/// full names of shortened names (cross-assembly-device-code.md §3.4).
/// </summary>
[Category(TestCategory.Compiler)]
public class DeviceImportAssemblerTests
{
    private const string Folder = "Generators/";

    private static readonly Dictionary<string, string> s_noExport =
        new() { ["build_property.GSharpExportDeviceCode"] = "false" };

    private static readonly Lazy<MetadataReference> s_library = new(static () =>
        BuildHandWritten("HandWrittenOne", "HandWrittenImports.cs", "ONE"));

    private static readonly Lazy<MetadataReference> s_other = new(static () =>
        BuildHandWritten("HandWrittenTwo", "HandWrittenDup.cs", "TWO"));

    // G failed for CUDA: the CUDA module leaves it out (the analyzer reports the use, GSH0089), the C++ one has it.
    // Mutant: Reach without FailedFor copies G's CUDA text
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_LeavesOutWhatFailedInTheDialect(bool isCuda)
    {
        var code = Resolve(isCuda, "__gsharp_fn_H__Lib__F(1)", s_library.Value);

        Assert.Contains(isCuda ? "/*F_CUDA_BODY*/" : "/*F_CPP_BODY*/", code.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("/*G_CUDA_BODY*/", code.Source, StringComparison.Ordinal);
        Assert.Equal(!isCuda, code.Source.Contains("/*G_CPP_BODY*/", StringComparison.Ordinal));
    }

    // F refers to S only in its CUDA text. Mutant: one reference list for both dialects
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_FollowsTheReferencesOfTheDialect(bool isCuda)
    {
        var code = Resolve(isCuda, "__gsharp_fn_H__Lib__F(1)", s_library.Value);

        Assert.Equal(isCuda, code.Source.Contains("/*S_CUDA_BODY*/", StringComparison.Ordinal));
        Assert.DoesNotContain("/*S_CPP_BODY*/", code.Source, StringComparison.Ordinal);
    }

    // The includes of the reached functions and structs, of the dialect. Mutant: CollectIncludes skips the structs
    [Theory]
    [InlineData(true, new[] { "<f_cuda.h>", "<s_cuda.h>" })]
    [InlineData(false, new[] { "<f_cpp.h>" })]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_CollectsTheIncludesOfStructsAndFunctions(bool isCuda, string[] expected)
    {
        var code = Resolve(isCuda, "__gsharp_fn_H__Lib__F(1)", s_library.Value);

        Assert.Equal(expected, code.Includes.AsImmutableArray().ToArray());
    }

    // m6: the full names of a shortened struct name and of a shortened constructor name, for the symbol file. Mutant:
    // CollectFullNames without the defined names
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_ListsTheFullNamesOfDefinedNames()
    {
        var code = Resolve(true, "__gsharp_fn_H__Lib__F(1)", s_library.Value);

        Assert.Equal("__gsharp_ty_H__S_full", code.FindFullName("__gsharp_ty_H__S"));
        Assert.Equal("__gsharp_ctor_H__S_full", code.FindFullName("__gsharp_ctor_H__S_ab12"));
    }

    // M1: Alpha names Zeta only through Zeta's constructor and sorts first by name. Mutant: OrderStructs without the
    // defined names
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_PlacesAStructAfterTheConstructorsItCalls(bool isCuda)
    {
        var code = Resolve(isCuda, "__gsharp_ty_H__Alpha x;", s_library.Value);

        var zeta = code.Source.IndexOf("/*ZETA_BODY*/", StringComparison.Ordinal);
        var alpha = code.Source.IndexOf("/*ALPHA_BODY*/", StringComparison.Ordinal);
        Assert.True(zeta >= 0 && zeta < alpha, code.Source);
    }

    // On the CPU dialects Relay's frame embeds Wait's, so Wait's body comes first; the declarations come before both.
    // Mutant: AppendFunctionBodies without the frame dependencies
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_PlacesTheBodyOfAnEmbeddedFrameFirst()
    {
        var code = Resolve(false, "__gsharp_fn_H__Lib__Relay(1)", s_library.Value);

        var declarations = Math.Max(code.Source.IndexOf("/*RELAY_SIGNATURE*/", StringComparison.Ordinal),
            code.Source.IndexOf("/*WAIT_SIGNATURE*/", StringComparison.Ordinal));
        var wait = code.Source.IndexOf("/*WAIT_BODY*/", StringComparison.Ordinal);
        var relay = code.Source.IndexOf("/*RELAY_BODY*/", StringComparison.Ordinal);
        Assert.True(declarations < wait && wait < relay, code.Source);
    }

    // M2: F's Dup is the one of F's assembly, although another assembly that records the same name comes first; the
    // module's own call takes the first. Mutant: Reach looks up every name in the first record of the name
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_TakesADependencyFromTheAssemblyThatRefersToIt(bool isCuda)
    {
        var throughF = Resolve(isCuda, "__gsharp_fn_H__Lib__F(1)", s_other.Value, s_library.Value);
        var direct = Resolve(isCuda, "__gsharp_fn_H__Lib__Dup(1)", s_other.Value, s_library.Value);

        Assert.Contains("/*DUP_BODY_ONE*/", throughF.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("/*DUP_BODY_TWO*/", throughF.Source, StringComparison.Ordinal);
        Assert.Contains("/*DUP_BODY_TWO*/", direct.Source, StringComparison.Ordinal);
    }

    // Minor 7 (3rd review): a struct used as a type whose text names a function that no record defines (Lost), or one
    // that two assemblies define (Twin, with the second library), is not imported. Mutants: IsUsable without either test
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Resolve_LeavesOutAStructWhoseDependencyCannotBeImported(bool isCuda)
    {
        var alone = Resolve(isCuda, "__gsharp_ty_H__Lost a; __gsharp_ty_H__Twin b;", s_library.Value);
        var withOther = Resolve(isCuda, "__gsharp_ty_H__Twin b;", s_other.Value, s_library.Value);

        Assert.DoesNotContain("/*LOST_BODY*/", alone.Source, StringComparison.Ordinal);
        Assert.Contains("/*TWIN_BODY*/", alone.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("/*TWIN_BODY*/", withOther.Source, StringComparison.Ordinal);
    }

    // The consumer declares H.Box<T> itself: the record of Box<int> (an ID of a constructed type) is not imported.
    // Mutant: ToDefinitionId returns the ID as it is
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Collect_LeavesOutAConstructedStructThatTheCompilationDeclares()
    {
        var records = DeviceImportAssembler.Collect(CreateConsumer(s_library.Value));

        Assert.Contains(records.AsImmutableArray(), static r => r.Record.Id == "T:H.Zeta");
        Assert.DoesNotContain(records.AsImmutableArray(), static r => r.Record.Id == "T:H.Box{System.Int32}");
    }

    private static ImportedDeviceCode Resolve(bool isCuda, string ownText, params MetadataReference[] libraries) =>
        DeviceImportAssembler.Resolve(DeviceImportAssembler.Collect(CreateConsumer(libraries)), isCuda, [ownText]);

    private static CSharpCompilation CreateConsumer(params MetadataReference[] libraries) =>
        DeviceLibraryBuilder.CreateConsumer(RoslynTestUtility.ReadTestCase(Folder + "HandWrittenImportsConsumer.cs"),
            libraries);

    private static MetadataReference BuildHandWritten(string assemblyName, string file, string marker) =>
        DeviceLibraryBuilder.Build(assemblyName, s_noExport,
            RoslynTestUtility.ReadTestCase(Folder + file)
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal)
                .Replace("ASSEMBLY_NAME", marker, StringComparison.Ordinal));
}
