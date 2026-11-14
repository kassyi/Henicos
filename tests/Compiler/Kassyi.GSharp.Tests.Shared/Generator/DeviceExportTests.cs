#nullable enable
using System.Globalization;
using System.IO;
using System.Reflection;
using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Hashing;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The device code export of a library (#433, cross-assembly-device-code.md §3.3): one
/// <c>[assembly: GpuDeviceExport]</c> per device function and per struct that device code uses or that a consumer could
/// not define from metadata, read back here from the compiled attributes as a consumer reads them.
/// </summary>
[Category(TestCategory.Compiler)]
public class DeviceExportTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly SyntaxTree s_libraryTree =
        RoslynTestUtility.GetTestCaseContext("Generators/DeviceExportLibrary.cs").Tree;

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceExport_LibraryCompilation_RecordsEveryDeviceFunction(string dialect)
    {
        var records = Run(dialect, out var result, out var output);

        Assert.Null(result.Exception);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        // A library without kernels gets the records and the managed state machines of its suspending functions
        Assert.Equal(["DeviceExportCase__LibMath.DeviceStateMachines.g.cs", DeviceExportEmitter.HintName],
            result.GeneratedSources.Select(static s => s.HintName).Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                "M:DeviceExportCase.LibMath.Bump(System.Int32@)",
                "M:DeviceExportCase.LibMath.Exchange(System.Int32[],System.Int32,System.Int32)",
                "M:DeviceExportCase.LibMath.Helper(System.Int32)",
                "M:DeviceExportCase.LibMath.Mu(System.UInt32)",
                "M:DeviceExportCase.LibMath.ReadAndClear(System.Int32@,System.Int32[])",
                "M:DeviceExportCase.LibMath.ReadUsed(DeviceExportCase.UsedOnly)",
                "M:DeviceExportCase.LibMath.Scale(System.Int32)",
                "M:DeviceExportCase.LibMath.Scale(System.Single)",
                "M:DeviceExportCase.LibMath.Scratch(System.Int32,System.Int32)",
                "M:DeviceExportCase.LibMath.SyncIfEven(System.Int32[],System.Int32)",
                "M:DeviceExportCase.LibMath.SyncWithin(System.Int32)",
                "M:DeviceExportCase.LibMath.TakeOne(System.Int32@)",
                "M:DeviceExportCase.LibMath.Twice(System.Int32)",
                "M:DeviceExportCase.LibMath.WriteThrough(System.Int32@,System.Int32[])"
            ],
            records.Where(static r => r.Kind == "Function").Select(static r => r.Id));

        Assert.All(records, static r =>
        {
            Assert.Equal(DeviceExportRecord.CurrentFormatVersion, r.FormatVersion);
            Assert.Equal(DeviceExportRecord.CurrentPreludeHash, r.PreludeHash);
            Assert.False(r.GetBool("CudaFailed"), r.Id);
            Assert.False(r.GetBool("CppFailed"), r.Id);
            Assert.NotEqual("", r.GetString("CudaBody"));
            Assert.NotEqual("", r.GetString("CppBody"));
        });
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceExport_LibraryCompilation_RecordsNamesFlagsAndEffects(string dialect)
    {
        var records = Run(dialect, out _, out _);
        ExportedRecord Find(string id) => records.Single(r => r.Id == id);

        var mu = Find("M:DeviceExportCase.LibMath.Mu(System.UInt32)");
        Assert.Equal("__gsharp_fn_DeviceExportCase__LibMath__Mu", mu.GetString("CppName"));
        Assert.False(mu.GetBool("Suspends"));
        Assert.Equal("None", mu.GetString("Effect"));

        // An internal helper is recorded too: the public caller refers to it by its C++ name
        var twice = Find("M:DeviceExportCase.LibMath.Twice(System.Int32)");
        Assert.Equal(["__gsharp_fn_DeviceExportCase__LibMath__Helper"], twice.GetArray("CudaReferences"));
        Assert.Equal(["__gsharp_fn_DeviceExportCase__LibMath__Helper"], twice.GetArray("CppReferences"));

        var exchange = Find("M:DeviceExportCase.LibMath.Exchange(System.Int32[],System.Int32,System.Int32)");
        Assert.True(exchange.GetBool("Suspends"));
        Assert.True(exchange.GetBool("ReachesBlockBarrier"));
        Assert.Equal("__gsharp_frame_DeviceExportCase__LibMath__Exchange", exchange.GetString("FrameName"));

        // C3: the library holds the managed state machine that a consumer's managed fallback calls; a function that does
        // not suspend has none
        Assert.True(exchange.GetBool("ManagedStateMachine"));
        Assert.False(mu.GetBool("ManagedStateMachine"));

        // B7: the parameters whose thread-variant argument makes a barrier diverge, and an early exit before a barrier
        var syncIfEven = Find("M:DeviceExportCase.LibMath.SyncIfEven(System.Int32[],System.Int32)");
        Assert.Equal(0b10, syncIfEven.GetInt("DivergentConditionParameters"));
        Assert.Equal(0, syncIfEven.GetInt("DivergentExitOnlyParameters"));
        Assert.False(syncIfEven.GetBool("HasExitOnlyBarrier"));
        var syncWithin = Find("M:DeviceExportCase.LibMath.SyncWithin(System.Int32)");
        Assert.Equal(0, syncWithin.GetInt("DivergentConditionParameters"));
        Assert.True(syncWithin.GetBool("HasExitOnlyBarrier"));
        Assert.Equal(0, exchange.GetInt("DivergentConditionParameters"));
        Assert.False(exchange.GetBool("HasExitOnlyBarrier"));

        // A ref parameter alone is RefParameters; a ref parameter and an array write is Memory
        Assert.Equal("RefParameters", Find("M:DeviceExportCase.LibMath.Bump(System.Int32@)").GetString("Effect"));
        Assert.Equal("Memory",
            Find("M:DeviceExportCase.LibMath.WriteThrough(System.Int32@,System.Int32[])").GetString("Effect"));

        // The overloads keep the names of their declaration order
        Assert.Equal("__gsharp_fn_DeviceExportCase__LibMath__Scale_o0",
            Find("M:DeviceExportCase.LibMath.Scale(System.Int32)").GetString("CppName"));
        Assert.Equal("__gsharp_fn_DeviceExportCase__LibMath__Scale_o1",
            Find("M:DeviceExportCase.LibMath.Scale(System.Single)").GetString("CppName"));

        // The CUDA text of a function that reaches dynamic shared memory takes the table; its site is recorded
        var scratch = Find("M:DeviceExportCase.LibMath.Scratch(System.Int32,System.Int32)");
        Assert.True(scratch.GetBool("ReachesDynamicSharedMemory"));
        Assert.Single(scratch.GetArray("DynamicSharedMemorySites"));
        Assert.StartsWith("template <typename TDsmem>", scratch.GetString("CudaBody"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void DeviceExport_LibraryCompilation_RecordsStructsApartFromFunctions(string dialect)
    {
        var records = Run(dialect, out _, out _);

        // UsedOnly through ReadUsed; Vec2 on its own (a constructor and a computed property); Plain not at all
        Assert.Equal(["T:DeviceExportCase.UsedOnly", "T:DeviceExportCase.Vec2"],
            records.Where(static r => r.Kind == "Struct").Select(static r => r.Id).Order(StringComparer.Ordinal));

        var readUsed = records.Single(static r => r.Id == "M:DeviceExportCase.LibMath.ReadUsed(DeviceExportCase.UsedOnly)");
        Assert.Contains("__gsharp_ty_DeviceExportCase__UsedOnly", readUsed.GetArray("CudaReferences"));
        Assert.Contains("__gsharp_ty_DeviceExportCase__UsedOnly", readUsed.GetArray("CppReferences"));
        Assert.DoesNotContain("__GSHARP_STRUCT_", readUsed.GetString("CudaBody"), StringComparison.Ordinal);
        Assert.DoesNotContain("__GSHARP_STRUCT_", readUsed.GetString("CppBody"), StringComparison.Ordinal);

        var used = records.Single(static r => r.Id == "T:DeviceExportCase.UsedOnly");
        Assert.StartsWith("#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceExportCase__UsedOnly__", used.GetString("CudaBody"),
            StringComparison.Ordinal);

        var vec2 = records.Single(static r => r.Id == "T:DeviceExportCase.Vec2");
        Assert.Equal(["__gsharp_ctor_DeviceExportCase__Vec2"], vec2.GetArray("DefinedNames"));
        Assert.Contains("__gsharp_ctor_DeviceExportCase__Vec2", vec2.GetString("CppBody"), StringComparison.Ordinal);
        Assert.Contains("__gsharp_mb_Sum", vec2.GetString("CudaBody"), StringComparison.Ordinal);
    }

    // The hash covers both preludes and the internal names without carriage returns, so a Windows and a WSL build agree;
    // a record carries it with the record layout, so text of another prelude, name encoding or layout is refused
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PreludeHash_CurrentPreludeDefinitions_CoversPreludesInternalNamesAndFormat()
    {
        var names = File.ReadAllText(RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Resources",
            "InternalNames.yml"));
        var hash = new Fnv1A64();
        foreach (var c in GSharpPreludeResources.Cuda + GSharpPreludeResources.Cpp + names)
        {
            if (c != '\r')
            {
                hash.Append(c);
            }
        }

        var sources = hash.Value.ToString("x16", CultureInfo.InvariantCulture);
        Assert.Equal(sources, GSharpPreludeResources.Hash);
        Assert.Equal(
            Fnv1A64.Compute($"{sources}/{DeviceExportRecord.CurrentFormatVersion}").ToString("x16", CultureInfo.InvariantCulture),
            DeviceExportRecord.CurrentPreludeHash);
    }

    // B1: the function's text keeps the definition of a struct that no record of the library holds (a Core vector
    // type), and does not name it as a reference to import
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void FunctionBody_UnrecordedStructs_KeepsStructsNotRecordedByLibrary(string dialect)
    {
        var dot3 = RunStructs(dialect).Single(static r => r.Id == "M:DeviceExportStructsCase.LibVector.Dot3(Kassyi.GSharp.Core.Float3)");

        foreach (var prefix in new[] { "Cuda", "Cpp" })
        {
            if (!dot3.GetString(prefix + "Signature").Contains(Float3Name, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains(Float3Guard, dot3.GetString(prefix + "Body"), StringComparison.Ordinal);
            Assert.DoesNotContain(Float3Name, dot3.GetArray(prefix + "References"));
        }

        Assert.Contains(Float3Name, dot3.GetString("CppSignature"), StringComparison.Ordinal);
    }

    // B1: a struct record keeps the definitions of the structs of its fields that no record holds, before its own
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_FieldDependencies_KeepsStructsOfItsFields(string dialect)
    {
        var particle = RunStructs(dialect).Single(static r => r.Id == "T:DeviceExportStructsCase.Particle");

        var body = particle.GetString("CppBody");
        var float3 = body.IndexOf(Float3Guard, StringComparison.Ordinal);
        var own = body.IndexOf("#ifndef __GSHARP_STRUCT___gsharp_ty_DeviceExportStructsCase__Particle__", StringComparison.Ordinal);
        Assert.True(float3 >= 0 && float3 < own, body);
        Assert.DoesNotContain(Float3Name, particle.GetArray("CppReferences"));
    }

    // A struct named only by a constructor of an exported struct (Box<int>) is recorded, and every name that a record
    // refers to is defined by a record of the library
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void DeviceExport_ReferencedStructTypes_RecordsEveryStructItsTextNames(string dialect)
    {
        var structs = RunStructs(dialect);
        var library = Run(dialect, out _, out _);

        Assert.Contains(structs, static r => r.Id == "T:DeviceExportStructsCase.Box{System.Int32}");
        foreach (var records in new[] { structs, library })
        {
            var defined = records.Select(static r => r.GetString("CppName"))
                .Concat(records.SelectMany(static r => r.GetArray("DefinedNames")))
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(records, r =>
            {
                Assert.All(r.GetArray("CudaReferences").Concat(r.GetArray("CppReferences")), name =>
                    Assert.True(defined.Contains(name), $"{r.Id} refers to {name}"));
            });
        }
    }

    // m5: a struct record keeps the includes that only its constructor needs; a sort no longer needs one (#625)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_SortingConstructor_NeedsNoInclude(string dialect)
    {
        var sortedPair = RunStructs(dialect).Single(static r => r.Id == "T:DeviceExportStructsCase.SortedPair");

        // MemoryExtensions.Sort calls the Prelude (#625): the constructor that sorts needs no include
        Assert.DoesNotContain("<thrust/sort.h>", sortedPair.GetArray("CudaIncludes"));
    }

    // Minor 1 (3rd review): a struct that no G# assembly records and that only the Dispose names stays in the text
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_DisposeDependencies_KeepsStructsItsDisposeNames(string dialect)
    {
        var lease = RunStructs(dialect).Single(static r => r.Id == "T:DeviceExportStructsCase.Lease3");

        Assert.Contains(Float3Guard, lease.GetString("CppBody"), StringComparison.Ordinal);
        Assert.Contains(Float3Name, lease.GetString("CppDispose"), StringComparison.Ordinal);
        Assert.DoesNotContain(Float3Name, lease.GetArray("CppReferences"));
    }

    // Minor 1 (2nd review): a struct whose Dispose cannot be emitted fails alone; the struct its text names does not
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_MissingNamedStructs_FailsWithoutNamedStructs(string dialect)
    {
        var records = Run(dialect, out _, out _, s_structsTree);

        var holder = records.Single(static r => r.Id == "T:DeviceExportStructsCase.Holder");
        var cell = records.Single(static r => r.Id == "T:DeviceExportStructsCase.Cell{System.Int32}");
        Assert.True(holder.GetBool("CudaFailed") && holder.GetBool("CppFailed"));
        Assert.False(cell.GetBool("CudaFailed") || cell.GetBool("CppFailed"));
    }

    // Minor 1 (2nd review): a function that fails does not fail the record of a struct it uses
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_FunctionEmission_DoesNotFailWithFunction(string dialect)
    {
        var records = Run(dialect, out _, out _, s_structsTree);

        var bad = records.Single(static r => r.Id == "M:DeviceExportStructsCase.LibVector.Bad(System.Int32)");
        var mark = records.Single(static r => r.Id == "T:DeviceExportStructsCase.LibVector.Mark");
        Assert.True(bad.GetBool("CudaFailed") && bad.GetBool("CppFailed"));
        Assert.False(mark.GetBool("CudaFailed") || mark.GetBool("CppFailed"));
    }

    // Minor 1 (2nd review): two records of one struct, each failed in the other dialect, give one record with both texts
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeviceExport_DialectOutputs_TakesEachDialectFromRecordThatDidNotFailIt()
    {
        var cudaOnly = CreateCudaOnlyStructRecord(internalError: "");
        var cppOnly = cudaOnly with
        {
            Cuda = cudaOnly.Cuda with { Body = "", Failed = true },
            Cpp = cudaOnly.Cpp with { Body = "/*CPP_TEXT*/", Failed = false }
        };

        var source = DeviceExportEmitter.Generate([], [cudaOnly, cppOnly], "0000000000000000");

        Assert.Contains("CudaBody = @\"/*CUDA_TEXT*/\"", source, StringComparison.Ordinal);
        Assert.Contains("CppBody = @\"/*CPP_TEXT*/\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed = true", source, StringComparison.Ordinal);
    }

    // Nit 2 (3rd review): the merged record keeps the message of a record only while it keeps that record's failure
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeviceExport_FailedExport_KeepsMessageOfFailureItKeeps()
    {
        var cudaOnly = CreateCudaOnlyStructRecord(internalError: "CUDA_ONLY_ERROR");
        var cppOnly = cudaOnly with
        {
            Cuda = cudaOnly.Cuda with { Body = "", Failed = true },
            Cpp = cudaOnly.Cpp with { Body = "/*CPP_TEXT*/", Failed = false },
            InternalError = ""
        };
        var neither = cudaOnly with
        {
            Cuda = cudaOnly.Cuda with { Body = "", Failed = true },
            InternalError = "BOTH_ERROR"
        };

        var merged = DeviceExportEmitter.Generate([], [cudaOnly, cppOnly], "0000000000000000");
        var stillFailed = DeviceExportEmitter.Generate([], [cudaOnly, neither], "0000000000000000");

        Assert.DoesNotContain("InternalError", merged, StringComparison.Ordinal);
        Assert.Contains("InternalError = @\"CUDA_ONLY_ERROR\"", stillFailed, StringComparison.Ordinal);
    }

    // A struct record with the CUDA text only, the C++ dialect failed
    private static DeviceExportRecord CreateCudaOnlyStructRecord(string internalError) =>
        new(
            DeviceExportRecord.StructKind,
            "T:N.S",
            new DeviceExportRecord.ExportNames(
                CppName: "__gsharp_ty_N__S",
                CppFullName: "",
                DefinedNames: default,
                DefinedIds: default,
                DefinedFullNames: default),
            Cuda: DeviceExportRecord.DialectText.Empty with { Body = "/*CUDA_TEXT*/" },
            Cpp: DeviceExportRecord.DialectText.NotTranspiled,
            DeviceExportRecord.FunctionTraits.None,
            internalError);

    // The names the emitter writes and the table reads are the named arguments of the attribute, all of them
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceExportAttributeNames_AllNames_AreTheSettablePropertiesOfTheAttribute()
    {
        var compilation = RoslynTestUtility.SharedCompilation;
        var attrSymbol = compilation.GetTypeByMetadataName("Kassyi.GSharp.Core.GpuDeviceExportAttribute")
            ?? throw new InvalidOperationException("GpuDeviceExportAttribute symbol not found");
        var namesSymbol = compilation.GetTypeByMetadataName("Kassyi.GSharp.Compiler.Models.DeviceExportAttributeNames")
            ?? throw new InvalidOperationException("DeviceExportAttributeNames symbol not found");
        var nestedNamesSymbol = compilation.GetTypeByMetadataName("Kassyi.GSharp.Compiler.Models.DeviceExportAttributeNames+Names")
            ?? throw new InvalidOperationException("Names symbol not found");
        var traitsSymbol = compilation.GetTypeByMetadataName("Kassyi.GSharp.Compiler.Models.DeviceExportAttributeNames+Traits")
            ?? throw new InvalidOperationException("Traits symbol not found");

        var constantSymbols = new[] { namesSymbol, nestedNamesSymbol, traitsSymbol };
        var constants = constantSymbols
            .SelectMany(static t => t.GetMembers().OfType<IFieldSymbol>())
            .Where(static f => f.IsConst)
            .Select(static f => f.ConstantValue as string ?? throw new InvalidOperationException(f.Name))
            .Concat(GetDialectNames(DeviceExportAttributeNames.Cuda))
            .Concat(GetDialectNames(DeviceExportAttributeNames.Cpp))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var properties = attrSymbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            .Select(static p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(properties, constants);
    }

    private static string[] GetDialectNames(DeviceExportAttributeNames.DialectNames names) =>
        [names.References, names.Signature, names.Body, names.Dispose, names.Includes, names.Failed];

    // Two functions write the struct of the guard, one without its Dispose: the record keeps the Dispose text
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void StructRecord_DisposalText_PrefersTextWithItsDispose(string dialect)
    {
        var guard = RunStructs(dialect).Single(static r => r.Id == "T:DeviceExportStructsCase.LibVector.Guard");

        Assert.NotEqual("", guard.GetString("CudaDispose"));
        Assert.NotEqual("", guard.GetString("CppDispose"));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceExport_ExportOptionDisabled_WritesNothing()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver("Cpu", new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" })
            .RunGenerators(compilation);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Empty(result.GeneratedSources);
    }

    private const string Float3Name = "__gsharp_ty_Kassyi__GSharp__Core__Float3";

    private const string Float3Guard = "#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Core__Float3__";

    private static readonly SyntaxTree s_structsTree =
        RoslynTestUtility.GetTestCaseContext("Generators/DeviceExportStructs.cs").Tree;

    private static readonly SyntaxTree s_managedVersionsTree =
        RoslynTestUtility.GetTestCaseContext("Generators/DeviceExportManagedVersionLibrary.cs").Tree;

    // #711: a library emits the managed versions of its functions that expand an operator macro on every dialect, with
    // the function's own accessibility, and records which functions have one; a consumer's managed code calls them
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceExport_ManagedCodeGeneration_RecordsAndEmitsManagedVersions(string dialect)
    {
        var records = Run(dialect, out var result, out var output, s_managedVersionsTree);

        Assert.Null(result.Exception);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        bool HasManagedVersion(string name) =>
            records.Single(r => r.Id.StartsWith($"M:DeviceExportManagedVersionCase.Ops.{name}(", StringComparison.Ordinal))
                .GetBool("ManagedVersion");
        Assert.True(HasManagedVersion("Twice"));
        Assert.True(HasManagedVersion("AddTwice"));
        Assert.False(HasManagedVersion("Add"));
        Assert.False(HasManagedVersion("Plain"));
        Assert.All(records, static r => Assert.False(r.GetBool("ManagedStateMachine"), r.Id));

        var versions = Assert.Single(result.GeneratedSources,
            static s => s.HintName == "DeviceExportManagedVersionCase__Ops.DeviceStateMachines.g.cs").SourceText.ToString();
        Assert.Contains(
            "internal static int __gsharp_managed_DeviceExportManagedVersionCase__Ops__Twice(ref global::DeviceExportManagedVersionCase.Counter counter, int a)",
            versions, StringComparison.Ordinal);
        Assert.Contains(
            "public static int __gsharp_managed_DeviceExportManagedVersionCase__Ops__AddTwice(ref global::DeviceExportManagedVersionCase.Counter counter, int a)",
            versions, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_managed_DeviceExportManagedVersionCase__Ops__Plain", versions,
            StringComparison.Ordinal);
    }

    private static List<ExportedRecord> RunStructs(string dialect)
    {
        var records = Run(dialect, out var result, out var output, s_structsTree);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        return records;
    }

    // M1 / Minor 5 / Minor 6 (3rd review): a CUDA library without kernels and without AllowUnsafeBlocks builds with its
    // state machines: as visible as each function, without unsafe and without warnings; left out (and recorded so)
    // where they could not compile
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StateMachines_LibraryWithoutKernels_CompilesWithoutKernels()
    {
        var compilation = CSharpCompilation.Create("StateMachineLibrary")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Enable))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(RoslynTestUtility.GetTestCaseContext("Generators/StateMachineVisibility.cs").Tree);
        var driver = CreateDriver("Cuda", null).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();

        Assert.Null(result.Exception);
        Assert.Empty(output.GetDiagnostics().Where(static d =>
            d.Severity == DiagnosticSeverity.Error ||
            (d.Severity == DiagnosticSeverity.Warning &&
             d.Location.SourceTree?.FilePath.EndsWith(".DeviceStateMachines.g.cs", StringComparison.Ordinal) == true)));

        var records = output.Assembly.GetAttributes()
            .Where(static a => a.AttributeClass?.Name == "GpuDeviceExportAttribute")
            .Select(static a => new ExportedRecord(a))
            .ToList();
        bool HasStateMachine(string id) => records.Single(r => r.Id == id).GetBool("ManagedStateMachine");
        Assert.True(HasStateMachine("M:StateMachineCase.LibSm.WaitPublic(System.Int32[],System.Int32)"));
        Assert.True(HasStateMachine("M:StateMachineCase.LibSm.WaitHidden(StateMachineCase.Hidden,System.Int32[],System.Int32)"));
        Assert.True(HasStateMachine("M:StateMachineCase.LibSm.Inner.WaitNested(System.Int32[],System.Int32)"));
        Assert.False(HasStateMachine("M:StateMachineCase.LibSm.WaitPrivate(System.Int32[],System.Int32)"));
        Assert.False(HasStateMachine("M:StateMachineCase.LibSm.WaitThrough(System.Int32[],System.Int32)"));
        // Private only through a spilled temporary: the record says so as well
        Assert.False(HasStateMachine("M:StateMachineCase.LibSm.WaitSpilled(System.Int32[],System.Int32)"));
        Assert.True(HasStateMachine("M:StateMachineCase.LibSm.WaitInner(System.Int32[],System.Int32)"));
        Assert.False(HasStateMachine("M:StateMachineCase.Closed.Open.WaitClosed(System.Int32[],System.Int32)"));

        var machines = string.Join("\n", result.GeneratedSources
            .Where(static s => s.HintName.EndsWith(".DeviceStateMachines.g.cs", StringComparison.Ordinal))
            .Select(static s => s.SourceText.ToString()));
        Assert.Contains("public static bool __gsharp_statemachine_StateMachineCase__LibSm__WaitPublic(", machines, StringComparison.Ordinal);
        Assert.Contains("internal static bool __gsharp_statemachine_StateMachineCase__LibSm__WaitHidden(", machines, StringComparison.Ordinal);
        Assert.Contains("internal class __gsharp_frame_StateMachineCase__LibSm__WaitHidden {", machines, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", machines, StringComparison.Ordinal);
        // A frame that holds a private nested type is private to its class (#1835); the records above say that no
        // other assembly can call it
        Assert.Contains("private static bool __gsharp_statemachine_StateMachineCase__LibSm__WaitPrivate(", machines,
            StringComparison.Ordinal);
        foreach (var name in new[] { "WaitPrivate", "WaitThrough", "WaitSpilled" })
        {
            Assert.Contains($"private class __gsharp_frame_StateMachineCase__LibSm__{name} {{", machines,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("WaitClosed", machines, StringComparison.Ordinal);
        var newLine = Environment.NewLine;
        Assert.Contains($"partial class LibSm{newLine}{{{newLine}public partial class Inner", machines, StringComparison.Ordinal);
    }

    private static List<ExportedRecord> Run(string dialect, out GeneratorRunResult result, out Compilation output,
        SyntaxTree? tree = null)
    {
        var driver = CreateDriver(dialect, null).RunGeneratorsAndUpdateCompilation(CreateCompilation(tree), out output, out _);
        result = driver.GetRunResult().Results.Single();
        return output.Assembly.GetAttributes()
            .Where(static a => a.AttributeClass?.Name == "GpuDeviceExportAttribute")
            .Select(static a => new ExportedRecord(a))
            .ToList();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree? tree = null) =>
        CSharpCompilation.Create("DeviceExportLibrary")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(tree ?? s_libraryTree);

    private static GeneratorDriver CreateDriver(string dialect, Dictionary<string, string>? options) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect, options));

    // One record as a consumer reads it: the constructor arguments and the named properties of the attribute
    private sealed class ExportedRecord(AttributeData attribute)
    {
        public int FormatVersion => (int)(attribute.ConstructorArguments[0].Value ?? 0);

        public string PreludeHash => (string)(attribute.ConstructorArguments[1].Value ?? "");

        public string Kind => (string)(attribute.ConstructorArguments[2].Value ?? "");

        public string Id => (string)(attribute.ConstructorArguments[3].Value ?? "");

        // A property left at its default (empty, false) is not written
        public string GetString(string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as string ?? "";

        public bool GetBool(string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is true;

        public int GetInt(string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is int value ? value : 0;

        public string[] GetArray(string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value is { Kind: TypedConstantKind.Array } array
                ? array.Values.Select(static v => (string)(v.Value ?? "")).ToArray()
                : [];
    }
}
