#nullable enable
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// A consumer of a G# library (#433, #538, cross-assembly-device-code.md §3.4–§3.6): the kernels call the library's
/// device functions, and the consumer's native module gets the text that the library recorded, reached from those calls,
/// with the library's names, flags and effects. The library is built in memory with its own generator run.
/// </summary>
[Category(TestCategory.Compiler)]
public class DeviceImportTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly Lazy<MetadataReference> s_library = new(static () =>
        DeviceLibraryBuilder.Build("DeviceExportLibrary", null,
            RoslynTestUtility.ReadTestCase("Generators/DeviceExportLibrary.cs")));

    private static readonly string s_consumer = RoslynTestUtility.ReadTestCase("Generators/DeviceImportConsumer.cs");

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_Consumer_BuildsWithoutDiagnostics(string dialect)
    {
        // The CPU wrapper's managed body calls the managed state machine of the suspending library function, which the
        // library emitted with its records
        var run = await KernelDiagnostics.RunCompilationAsync(
            DeviceLibraryBuilder.CreateConsumer(s_consumer, s_library.Value), dialect);

        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Generator.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // Only what the kernels reach: Twice and the internal Helper it calls, not Mu
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Imports_TheReachedFunctionsOnly(string dialect)
    {
        var (device, _) = Generate(dialect);

        Assert.Contains("__gsharp_fn_DeviceExportCase__LibMath__Twice(int __gsharp_id_0_x){", device, StringComparison.Ordinal);
        Assert.Contains("__gsharp_fn_DeviceExportCase__LibMath__Helper(int __gsharp_id_0_x){", device, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_fn_DeviceExportCase__LibMath__Mu", device, StringComparison.Ordinal);
    }

    // The library's name of the second overload, which metadata alone cannot order
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_Overload_UsesTheLibrarysName(string dialect)
    {
        var (device, kernels) = Generate(dialect);

        Assert.Contains("__gsharp_fn_DeviceExportCase__LibMath__Scale_o1(__gsharp_id_0_data[", kernels, StringComparison.Ordinal);
        Assert.Contains("float __gsharp_fn_DeviceExportCase__LibMath__Scale_o1(float __gsharp_id_0_x){", device,
            StringComparison.Ordinal);
    }

    // The library declares Pick(int) in z_first.cs, added first, and Pick(float) in a_second.cs: it orders the overloads
    // by path (Pick(float) is _o0), while metadata lists Pick(int) first. The names follow the library
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_OverloadsInTwoFiles_FollowTheLibrarysOrder(string dialect)
    {
        var library = DeviceLibraryBuilder.BuildFiles("OverloadLibrary", null,
            ("z_first.cs", RoslynTestUtility.ReadTestCase("Generators/DeviceImportOverloads_First.cs")),
            ("a_second.cs", RoslynTestUtility.ReadTestCase("Generators/DeviceImportOverloads_Second.cs")));
        var compilation = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Generators/DeviceImportOverloadsConsumer.cs"), library);

        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var kernels = result.GeneratedSources
            .Single(static s => s.HintName.EndsWith("OverloadKernels.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();

        Assert.Contains("__gsharp_fn_OverloadLibrary__LibPick__Pick_o1(__gsharp_id_0_ints[", kernels, StringComparison.Ordinal);
        Assert.Contains("__gsharp_fn_OverloadLibrary__LibPick__Pick_o0(__gsharp_id_1_floats[", kernels, StringComparison.Ordinal);
        Assert.Contains("int __gsharp_fn_OverloadLibrary__LibPick__Pick_o1(int __gsharp_id_0_x){", device, StringComparison.Ordinal);
    }

    // The struct with its constructor and computed property comes before the functions that use it
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Struct_ImportedBeforeTheFunctions(string dialect)
    {
        var (device, kernels) = Generate(dialect);

        var definition = device.IndexOf("#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceExportCase__Vec2__", StringComparison.Ordinal);
        var function = device.IndexOf("__gsharp_fn_DeviceExportCase__LibMath__Twice(int __gsharp_id_0_x){", StringComparison.Ordinal);
        Assert.True(definition >= 0 && definition < function, device);
        Assert.Contains("__gsharp_ctor_DeviceExportCase__Vec2(", kernels, StringComparison.Ordinal);
        Assert.Contains(".__gsharp_mb_Sum()", kernels, StringComparison.Ordinal);
    }

    // #538: the library's function reaches barriers, so on the CPU dialects the kernel keeps its frame and resumes it
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SuspendingFunction_IsACallThatSuspends()
    {
        var (device, kernels) = Generate("Cpu");

        Assert.Contains("struct __gsharp_frame_DeviceExportCase__LibMath__Exchange {", device, StringComparison.Ordinal);
        Assert.Contains("__gsharp_frame_DeviceExportCase__LibMath__Exchange __gsharp_call_frame_0;", kernels,
            StringComparison.Ordinal);
        Assert.Contains("&__gsharp_ctx->__gsharp_call_frame_0", kernels, StringComparison.Ordinal);
    }

    // The CUDA kernel owns the slot of the library's dynamic declaration and passes the table on
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DynamicSharedMemory_KernelOwnsTheLibrarysSlot()
    {
        var (device, kernels) = Generate("Cuda");

        Assert.Contains("reaches 1 dynamic shared memory declaration", kernels, StringComparison.Ordinal);
        Assert.Contains(",__gsharp_dsmem)", kernels, StringComparison.Ordinal);
        Assert.Contains("template <typename TDsmem> ", device, StringComparison.Ordinal);
    }

    // The library's effect decides the order: ReadAndClear writes memory that the right operand reads, TakeOne does not
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_Effect_SequencesOnlyTheMemoryWritingCall(string dialect)
    {
        var (_, kernels) = Generate(dialect);
        var lines = kernels.Split('\n');

        // The C++ lines (the CPU wrapper also holds the managed body)
        var readAndClear = lines.Single(static l => l.Contains("__gsharp_fn_DeviceExportCase__LibMath__ReadAndClear(", StringComparison.Ordinal));
        var takeOne = lines.Single(static l => l.Contains("__gsharp_fn_DeviceExportCase__LibMath__TakeOne(", StringComparison.Ordinal));
        Assert.Contains("__gsharp_seq_", readAndClear, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_seq_", takeOne, StringComparison.Ordinal);
    }

    // An edit of a consumer kernel leaves the records of the references as they were
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_EditingTheConsumer_KeepsTheImportTableCached(string dialect)
    {
        var compilation = DeviceLibraryBuilder.CreateConsumer(s_consumer, s_library.Value);
        var driver = CreateDriver(dialect).RunGenerators(compilation);

        var tree = compilation.SyntaxTrees.Single();
        var edited = compilation.ReplaceSyntaxTree(tree,
            tree.WithChangedText(tree.GetText().Replace(tree.ToString().IndexOf("* 10", StringComparison.Ordinal), 4, "* 11")));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        Assert.Null(result.Exception);
        Assert.All(result.TrackedSteps[GpuKernelGenerator.DeviceImportTableTrackingName].SelectMany(static run => run.Outputs),
            static output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                output.Reason.ToString()));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_StructsConsumer_BuildsWithoutDiagnostics(string dialect)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(
            DeviceLibraryBuilder.CreateConsumer(s_structsConsumer, s_structsLibrary.Value), dialect);

        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Generator.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // M1: the declarations of the functions, then the structs (whose constructors call them), then the Dispose
    // definitions, then the bodies
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_ImportOrder_DeclarationsStructsDisposesBodies(string dialect)
    {
        var (device, _) = GenerateStructs(dialect);

        var declaration = IndexOf(device, "__gsharp_fn_DeviceImportStructsCase__LibStructs__Twice(int __gsharp_id_0_x);");
        var scaled = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceImportStructsCase__Scaled__");
        var lease = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceImportStructsCase__Lease__");
        var dispose = IndexOf(device, "#ifndef __GSHARP_DISPOSE___gsharp_ty_DeviceImportStructsCase__Lease__");
        var body = IndexOf(device, "__gsharp_fn_DeviceImportStructsCase__LibStructs__Twice(int __gsharp_id_0_x){");
        Assert.True(declaration < scaled && scaled < dispose && lease < dispose && dispose < body,
            $"{declaration} {scaled} {lease} {dispose} {body}\n{device}");
    }

    // M1, m8: a struct comes after the struct of its field (Omega) and after the struct whose constructor it calls
    // (Zeta), although it sorts first by name
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_ImportOrder_StructAfterItsFieldsAndConstructors(string dialect)
    {
        var (device, _) = GenerateStructs(dialect);

        var alpha = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceImportStructsCase__Alpha__");
        var omega = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceImportStructsCase__Omega__");
        var zeta = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceImportStructsCase__Zeta__");
        Assert.True(omega < alpha && zeta < alpha, $"{omega} {zeta} {alpha}\n{device}");
    }

    // m8: the frame of an imported suspending function embeds the frame of the imported function it calls, whose
    // body comes first on the CPU dialects
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ImportOrder_CalleeFrameFirst()
    {
        var (device, _) = GenerateStructs("Cpu");

        var wait = IndexOf(device, "struct __gsharp_frame_DeviceImportStructsCase__LibStructs__Wait {");
        var relay = IndexOf(device, "struct __gsharp_frame_DeviceImportStructsCase__LibStructs__Relay {");
        Assert.True(wait < relay, $"{wait} {relay}");
    }

    // M4, m8: a constructor of an imported generic struct and the two constructors of an imported struct take the
    // names that the library gave them
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_Constructors_TakeTheLibrarysNames(string dialect)
    {
        var (_, kernels) = GenerateStructs(dialect);

        Assert.Contains("__gsharp_ctor_DeviceImportStructsCase__Range2_o0(1)", kernels, StringComparison.Ordinal);
        Assert.Contains("__gsharp_ctor_DeviceImportStructsCase__Range2_o1(2, 3)", kernels, StringComparison.Ordinal);
        Assert.Contains("__gsharp_ctor_DeviceImportStructsCase__Box_g1_bSystem__Int32_e(1)", kernels, StringComparison.Ordinal);
    }

    // m2: an imported constructor with a body writes what its record does not say; the call is sequenced before the
    // read of data[0]
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_Effect_ImportedConstructorIsSequenced(string dialect)
    {
        var (_, kernels) = GenerateStructs(dialect);

        var line = kernels.Split('\n')
            .Single(static l => l.Contains("__gsharp_ctor_DeviceImportStructsCase__Recorder(", StringComparison.Ordinal));
        Assert.Contains("__gsharp_seq_", line, StringComparison.Ordinal);
    }

    // B1: on the C++ dialect Float3 is a struct of the text, which comes with the imported function before its body;
    // CUDA maps it to float3
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_CoreStructOfAnImportedFunction_IsDefinedBeforeItsBody(string dialect)
    {
        var (device, _) = GenerateStructs(dialect);

        if (dialect == "Cuda")
        {
            Assert.Contains("__gsharp_fn_DeviceImportStructsCase__LibStructs__Dot3(float3 __gsharp_id_0_v);", device,
                StringComparison.Ordinal);
            return;
        }

        var body = IndexOf(device, "__gsharp_fn_DeviceImportStructsCase__LibStructs__Dot3(__gsharp_ty_Kassyi__GSharp__Core__Float3 __gsharp_id_0_v){");
        Assert.True(IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Core__Float3__") < body, device);
    }

    // The table of a library that an IDE gives as a referenced compilation (its symbols have declarations, outside
    // the consumer): the records are read from its source attributes, the names follow its declarations
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_CompilationReference_ImportsAsAMetadataReference(string dialect)
    {
        var library = CSharpCompilation.Create("DeviceExportLibrary")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(RoslynTestUtility.ReadTestCase("Generators/DeviceExportLibrary.cs"),
                s_parseOptions, path: "library.cs"));
        CreateDriver("Cuda").RunGeneratorsAndUpdateCompilation(library, out var output, out _);
        var reference = output.ToMetadataReference();
        Assert.IsAssignableFrom<CompilationReference>(reference);

        var compilation = DeviceLibraryBuilder.CreateConsumer(s_consumer, reference);
        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();
        var kernels = result.GeneratedSources
            .Single(static s => s.HintName.EndsWith("ImportKernels.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();

        Assert.Contains("__gsharp_fn_DeviceExportCase__LibMath__Helper(int __gsharp_id_0_x){", device, StringComparison.Ordinal);
        Assert.Contains("__gsharp_fn_DeviceExportCase__LibMath__Scale_o1(__gsharp_id_0_data[", kernels, StringComparison.Ordinal);
        Assert.Contains("__gsharp_ctor_DeviceExportCase__Vec2(", kernels, StringComparison.Ordinal);

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // Major 1 (2nd review): B's functions use A's structs, which A's records hold; the application imports A's records
    // (the computed property and the Dispose) through B's references, not B's copy of the fields
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StructOfAnotherLibrary_ComesFromItsOwnRecord(string dialect)
    {
        var a = DeviceLibraryBuilder.Build("XasmA", null, RoslynTestUtility.ReadTestCase("Generators/ThreeAssemblies_A.cs"));
        var b = DeviceLibraryBuilder.BuildWithReferences("XasmB", null, [a],
            RoslynTestUtility.ReadTestCase("Generators/ThreeAssemblies_B.cs"));
        var compilation = DeviceLibraryBuilder.CreateConsumer(RoslynTestUtility.ReadTestCase("Generators/ThreeAssemblies_App.cs"), b, a);

        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();

        var vec2 = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmA__Vec2__");
        var sum = IndexOf(device, "__gsharp_mb_Sum() const");
        Assert.True(vec2 < sum, device);
        Assert.Equal(1, CountOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmA__Vec2__"));
        IndexOf(device, "#ifndef __GSHARP_DISPOSE___gsharp_ty_XasmA__Lease__");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // Major 2 (2nd review): the kernel uses Alpha as a type only; its record's computed property calls Helper, which
    // failed for C++: the C++ module does not import the record and defines Alpha from metadata, as before the import
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StructUsedAsATypeOnly_IsNotImportedWhenItsDependencyCannotBe(string dialect, bool imported)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport,
            RoslynTestUtility.ReadTestCase("Generators/HandWrittenStruct.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));
        var compilation = DeviceLibraryBuilder.CreateConsumer(
            RoslynTestUtility.ReadTestCase("Generators/HandWrittenStructConsumer.cs"), library);

        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();
        var kernels = result.GeneratedSources
            .Single(static s => s.HintName.EndsWith("AlphaKernels.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();

        Assert.Equal(imported, device.Contains("__gsharp_mb_Probe", StringComparison.Ordinal));
        Assert.Equal(imported, device.Contains("__gsharp_fn_XasmLib__LibMath__Helper", StringComparison.Ordinal));
        Assert.Contains("struct __gsharp_ty_XasmLib__Alpha", device + kernels, StringComparison.Ordinal);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // Minor 2 (3rd review): A's record of Lease failed for C++, so B keeps Lease's definition in Hold's C++ text; the
    // CUDA text refers to A's record
    [Theory]
    [InlineData("Cpu", true)]
    [InlineData("Cuda", false)]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StructWhoseRecordFailedInTheDialect_StaysInTheText(string dialect, bool kept)
    {
        var a = DeviceLibraryBuilder.Build("XasmLib", s_noExport,
            RoslynTestUtility.ReadTestCase("Diagnostics/CrossAssembly/HandWrittenLease.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));
        var b = DeviceLibraryBuilder.BuildWithReferences("XasmB", null, [a],
            RoslynTestUtility.ReadTestCase("Generators/FailedStructLibraryB.cs"));
        var compilation = DeviceLibraryBuilder.CreateConsumer(RoslynTestUtility.ReadTestCase("Generators/FailedStructApp.cs"), b, a);

        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();

        var lease = IndexOf(device, "#ifndef __GSHARP_STRUCT___gsharp_ty_XasmLib__Lease__");
        var hold = IndexOf(device, "__gsharp_fn_XasmB__LibB__Hold(int __gsharp_id_0_x){");
        Assert.True(lease < hold, device);
        // A's record (with its Dispose definition) only where it can be used
        Assert.Equal(!kept, device.Contains("__gsharp_ty_XasmLib__Lease::__gsharp_dispose(){", StringComparison.Ordinal));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    private static readonly Dictionary<string, string> s_noExport =
        new() { ["build_property.GSharpExportDeviceCode"] = "false" };

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static readonly Lazy<MetadataReference> s_structsLibrary = new(static () =>
        DeviceLibraryBuilder.Build("DeviceImportStructsLibrary", null,
            RoslynTestUtility.ReadTestCase("Generators/DeviceImportStructsLibrary.cs")));

    private static readonly string s_structsConsumer = RoslynTestUtility.ReadTestCase("Generators/DeviceImportStructsConsumer.cs");

    private static int IndexOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' is missing:\n{text}");
        return index;
    }

    private static (string Device, string Kernels) GenerateStructs(string dialect) =>
        Generate(dialect, s_structsConsumer, s_structsLibrary.Value, "StructImportKernels.g.cs");

    // The shared device code (with the imports) and the kernels' sources, from the GSharpEmitSource constants
    private static (string Device, string Kernels) Generate(string dialect) =>
        Generate(dialect, s_consumer, s_library.Value, "ImportKernels.g.cs");

    private static (string Device, string Kernels) Generate(string dialect, string consumer, MetadataReference library,
        string kernelsHintSuffix)
    {
        var compilation = DeviceLibraryBuilder.CreateConsumer(consumer, library);
        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var device = result.GeneratedSources
            .Single(s => s.HintName == $"GSharpShared{dialect}DeviceCode.g.cs")
            .SourceText.ToString();
        var kernels = result.GeneratedSources
            .Single(s => s.HintName.EndsWith(kernelsHintSuffix, StringComparison.Ordinal))
            .SourceText.ToString();
        return (device, kernels);
    }

    private static GeneratorDriver CreateDriver(string dialect) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
}
