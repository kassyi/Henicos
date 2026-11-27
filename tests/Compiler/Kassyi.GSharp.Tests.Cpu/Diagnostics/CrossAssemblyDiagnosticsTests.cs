#nullable enable
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Transpiler;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Device code of another assembly that the native module cannot import is reported at its use (#433, #538): an
/// assembly that recorded nothing (GSH0088), a record of another G# or one that the library's build could not
/// transpile (GSH0089), and two assemblies that give one C++ name (GSH0090). The libraries are built in memory.
/// </summary>
[Category(TestCategory.Compiler)]
public class CrossAssemblyDiagnosticsTests
{
    private const string Folder = "Diagnostics/CrossAssembly/";

    private static readonly string s_library = RoslynTestUtility.ReadTestCase(Folder + "Library.cs");
    private static readonly string s_consumer = RoslynTestUtility.ReadTestCase(Folder + "Consumer.cs");

    private static readonly string s_conversionLibrary =
        RoslynTestUtility.ReadTestCase(Folder + "ConversionLibrary.cs");
    private static readonly string s_conversionConsumer =
        RoslynTestUtility.ReadTestCase(Folder + "ConversionConsumer.cs");

    private static readonly Dictionary<string, string> s_noExport =
        new() { ["build_property.GSharpExportDeviceCode"] = "false" };

    // Line 11: LibMath.Twice(...), the name of the call; line 12: new Pair(...), the new keyword of the creation
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task LibraryWithoutRecords_ForeignAssemblyReference_ReportsGsh0088AtEachUse(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library);

        var diagnostics = await RunAsync(dialect, s_consumer, library);

        Assert.Equal([(Analyzer, 11, 43, 11, 48), (Analyzer, 12, 24, 12, 27)],
            diagnostics.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id));
        Assert.Contains(diagnostics, static d =>
            d.Id == GSharpDiagnostics.DeviceFunctionNotExported.Id && d.Message.Contains("'LibMath.Twice' is declared in 'XasmLib'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task LibraryWithRecords_ValidExportRecords_ReportsNothing(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", null, s_library);

        var diagnostics = await RunAsync(dialect, s_consumer, library);

        Assert.Empty(diagnostics.Where(static d => d.Id is "GSH0088" or "GSH0089" or "GSH0090"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RecordOfAnotherPrelude_MismatchedPreludeHash_ReportsGsh0089(string dialect)
    {
        var library = BuildHandWritten(preludeHash: "0123456789abcdef", cudaFailed: false, cppFailed: false);

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        Assert.Equal([(Analyzer, 11, 43, 11, 48)],
            diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "written by another version of G#"));
    }

    // The CUDA consumer meets the CUDA failure, the CPU consumer the C++ one; the other dialect's failure is not its own
    [Theory]
    [InlineData("Cpu", false, true, 1)]
    [InlineData("Cpu", true, false, 0)]
    [InlineData("Cuda", true, false, 1)]
    [InlineData("Cuda", false, true, 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task RecordThatFailedInTheDialect_DialectCompilationFailure_ReportsGsh0089(
        string dialect,
        bool cudaFailed,
        bool cppFailed,
        int expected)
    {
        var library = BuildHandWritten(preludeHash: null, cudaFailed, cppFailed);

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "could not transpile it");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
    }

    // Two libraries declare XasmLib.LibMath.Twice: the consumer tells them apart with an extern alias, C++ cannot
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TwoLibrariesWithOnePath_ConflictingFilePaths_ReportsGsh0090(string dialect)
    {
        var first = DeviceLibraryBuilder.Build("LibA", null, s_library).WithAliases(["LibA"]);
        var second = DeviceLibraryBuilder.Build("LibB", null, s_library).WithAliases(["LibB"]);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "CollisionConsumer.cs"),
            first, second);

        Assert.Equal([(Analyzer, 11, 57, 11, 62)], diagnostics.Ranges(GSharpDiagnostics.DeviceExportNameCollision.Id));
        Assert.Contains(diagnostics, static d => d.Id == GSharpDiagnostics.DeviceExportNameCollision.Id &&
                                                 d.Message.Contains("'__gsharp_fn_XasmLib__LibMath__Twice'", StringComparison.Ordinal));
    }

    // Minor 3 (2nd review): a struct used as a type only (a local of the type, default) whose path two references
    // declare, each with a record
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TypeWithPathOfTwoLibraries_PathCollision_ReportsGsh0090(string dialect)
    {
        var first = DeviceLibraryBuilder.Build("LibA", null, s_library).WithAliases(["LibA"]);
        var second = DeviceLibraryBuilder.Build("LibB", null, s_library).WithAliases(["LibB"]);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "TypeUseConsumer.cs"), first,
            second);

        Assert.Equal([(Analyzer, 11, 13, 11, 31)], diagnostics.Ranges(GSharpDiagnostics.DeviceExportNameCollision.Id));
    }

    // Minor 4 (3rd review): a generic struct of two libraries, and a field of the consumer's own struct
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task GenericTypeAndFieldWithPathOfTwoLibraries_PathCollision_ReportsGsh0090(string dialect)
    {
        var first = DeviceLibraryBuilder.Build("LibA", null, RoslynTestUtility.ReadTestCase(Folder + "BoxLibrary.cs"))
            .WithAliases(["LibA"]);
        var second = DeviceLibraryBuilder.Build("LibB", null, RoslynTestUtility.ReadTestCase(Folder + "BoxLibrary.cs"))
            .WithAliases(["LibB"]);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "TypeUseGenericConsumer.cs"),
            first, second);

        Assert.Equal([(Analyzer, 16, 13, 16, 35), (Analyzer, 17, 13, 17, 17)],
            diagnostics.Ranges(GSharpDiagnostics.DeviceExportNameCollision.Id));
    }

    // Minor 7 (3rd review): the message names the exception of the library's generator, at the call
    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task RecordWithAnInternalError_ExportRecordFailure_ReportsGsh0089WithErrorMessage(
        string dialect,
        int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenInternalError.cs"));

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id,
            "the generator failed with InvalidOperationException: boom");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
    }

    // Nit 5 (2nd review): a using of a struct whose record failed for the dialect is GSH0089, not GSH0003
    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task UsingOfStructThatFailedInTheDialect_FailedTypeUsage_ReportsGsh0089(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport,
            HandWrittenExportRecords.Read(Folder + "HandWrittenLease.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "LeaseConsumer.cs"), library);

        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id).Count);
        Assert.All(diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id),
            static range => Assert.Equal((Analyzer, 11, 13, 11, 18), range));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // Nit 4 (3rd review): a device function of a consumer that exports its device code has a state machine on CUDA as
    // well, which calls the library's
    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 1)]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingCallFromExportedDeviceFunction_CrossAssemblyInvocation_ReportsGsh0089(
        string dialect,
        int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenSuspendingRecord.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "DeviceFunctionConsumer.cs"),
            library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "managed state machine");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 28, 11, 33), range));
    }

    // C3: a suspending function whose library holds no managed state machine cannot run on the managed fallback of the
    // CPU dialect; CUDA does not need one
    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingFunctionWithoutManagedStateMachine_MissingStateMachine_ReportsGsh0089(
        string dialect,
        int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenSuspendingRecord.cs"));

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "managed state machine");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
    }

    // M3: a target-typed new is a creation as well, reported at its new keyword
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TargetTypedNew_UnrecordedLibraryType_ReportsGsh0088(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "ImplicitNewConsumer.cs"), library);

        Assert.Equal([(Analyzer, 11, 25, 11, 28)], diagnostics.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id));
    }

    // M2: Twice is usable, the Helper it calls failed for C++: the C++ consumer meets it at the call of Twice
    [Theory]
    [InlineData("Cpu", 1)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task DependencyThatFailedInTheDialect_TransitiveUsage_ReportsGsh0089AtUse(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenChain.cs"));

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "could not transpile it");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
        Assert.Equal(expected, diagnostics.Count(static d =>
            d.Id == GSharpDiagnostics.DeviceExportUnusable.Id && d.Message.Contains("'LibMath.Helper'", StringComparison.Ordinal)));
    }

    // M2: IncTwice calls a function of a third assembly, which exports nothing (GSH0088 naming that function) or
    // exports it (nothing to report)
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", false)]
    [InlineData("Cpu", true)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.SadPath)]
    public async Task DependencyInThirdAssembly_UnexportedTypeReference_ReportsGsh0088(
        string dialect,
        bool innerExports)
    {
        var inner = DeviceLibraryBuilder.Build("XasmInner", innerExports ? null : s_noExport,
            RoslynTestUtility.ReadTestCase(Folder + "InnerLibrary.cs"));
        var middle = DeviceLibraryBuilder.BuildWithReferences("XasmMiddle", null, [inner],
            RoslynTestUtility.ReadTestCase(Folder + "MiddleLibrary.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "MiddleConsumer.cs"), middle,
            inner);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id);
        if (innerExports)
        {
            Assert.Empty(ranges);
        }
        else
        {
            Assert.Equal([(Analyzer, 10, 56, 10, 64)], ranges);
        }

        Assert.Equal(innerExports ? 0 : 1, diagnostics.Count(static d =>
            d.Id == GSharpDiagnostics.DeviceFunctionNotExported.Id &&
            d.Message.Contains("'LibInner.Inc' is declared in 'XasmInner'", StringComparison.Ordinal)));
        Assert.Empty(diagnostics.Where(static d => d.Id is "GSH0089" or "GSH0090"));
    }

    // M2: Quad's internal Helper has the path of a function of another library, which gives it the same C++ name
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DependencyWithPathOfAnotherLibrary_TransitivePathCollision_ReportsGsh0090AtUse(string dialect)
    {
        var chain = DeviceLibraryBuilder.Build("LibA", null, RoslynTestUtility.ReadTestCase(Folder + "ChainLibrary.cs"))
            .WithAliases(["LibA"]);
        var helper = DeviceLibraryBuilder.Build("LibB", null, RoslynTestUtility.ReadTestCase(Folder + "HelperLibrary.cs"))
            .WithAliases(["LibB"]);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "ChainConsumer.cs"), chain,
            helper);

        Assert.Equal([(Analyzer, 11, 57, 11, 61)], diagnostics.Ranges(GSharpDiagnostics.DeviceExportNameCollision.Id));
        Assert.Contains(diagnostics, static d => d.Id == GSharpDiagnostics.DeviceExportNameCollision.Id &&
                                                 d.Message.Contains("'LibMath.Helper' of 'LibA' and the same path in 'LibB'", StringComparison.Ordinal));
    }

    // M2: the consumer declares the path of Quad's internal Helper itself, whose own device code takes the same name
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DependencyWithPathOfTheConsumer_ConsumerPathCollision_ReportsGsh0090AtUse(string dialect)
    {
        var chain = DeviceLibraryBuilder.Build("LibA", null, RoslynTestUtility.ReadTestCase(Folder + "ChainLibrary.cs"))
            .WithAliases(["LibA"]);

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "SamePathConsumer.cs"), chain);

        Assert.Equal([(Analyzer, 11, 57, 11, 61)], diagnostics.Ranges(GSharpDiagnostics.DeviceExportNameCollision.Id));
        Assert.Contains(diagnostics, static d => d.Id == GSharpDiagnostics.DeviceExportNameCollision.Id &&
                                                 d.Message.Contains("'LibMath.Helper' of 'LibA' and the same path in 'DeviceImportConsumer'", StringComparison.Ordinal));
    }

    // M4: the library records Box<int> only; Box<float>'s constructor has no record (GSH0088), Box<int>'s has.
    // m4: an iterator of the library is GSH0003, without GSH0088. m1: a device function of the consumer that calls the
    // library's grid-wide function is parallel-dependent (GSH0035)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task GenericIteratorAndGridFunctions_CrossAssemblyUsage_ReportsDiagnosticsOnce(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", null, RoslynTestUtility.ReadTestCase(Folder + "BoxLibrary.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "BoxConsumer.cs"), library);

        Assert.Equal([(Analyzer, 12, 25, 12, 28)], diagnostics.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id));
        Assert.Contains(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id), static range => range.StartLine == 20);
        Assert.Equal([(Analyzer, 33, 17, 33, 37)],
            diagnostics.Ranges(GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext.Id));
    }

    // The record of a function of another assembly carries one bit per parameter whose variance makes its barrier
    // diverge: the call with the thread index (line 11) is GSH0083, the one with a constant (line 12) is not
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ImportedDivergentParameter_CrossAssemblyDeviceFunction_ReportsGsh0083(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenDivergentRecord.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "DivergentCallConsumer.cs"),
            library);

        Assert.Equal([(Analyzer, 11, 23, 11, 51)],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
    }

    // A record written before ReachesGeneratedMeaning existed has no such key; its ManagedVersion then meant that the
    // function reaches code whose C# meaning only the generated code gives, so it reads as that. A record that has the
    // key is read as written, also when the key says false beside ManagedVersion = true
    [Theory]
    [InlineData("", false)]
    [InlineData(", ManagedVersion = true", true)]
    [InlineData(", ManagedVersion = true, ReachesGeneratedMeaning = false", false)]
    [InlineData(", ReachesGeneratedMeaning = true", true)]
    [Scenario(TestScenario.Boundary)]
    public void ImportedRecord_WithoutReachesGeneratedMeaningKey_FallsBackToManagedVersion(string traits, bool expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenTraitRecord.cs")
                .Replace("TRAITS", traits, StringComparison.Ordinal));
        var compilation = DeviceLibraryBuilder.CreateConsumer(s_consumer, library);

        var assembly = Assert.IsAssignableFrom<IAssemblySymbol>(compilation.GetAssemblyOrModuleSymbol(library));

        var imported = Assert.Single(DeviceExportTable.ForAssembly(assembly).Records);
        Assert.Equal(expected, imported.Record.Traits.ReachesGeneratedMeaning);
    }

    // #1991: a suspending function of another assembly whose record sets ManagedStateMachine and PrivateManagedFrame is
    // run through its generated entries (Entry); without PrivateManagedFrame it is held by its frame type (Direct);
    // without ManagedStateMachine it has no managed frame (Unavailable)
    [Theory]
    [InlineData(", ManagedStateMachine = true, PrivateManagedFrame = true", "Entry")]
    [InlineData(", ManagedStateMachine = true", "Direct")]
    [InlineData(", PrivateManagedFrame = true", "Unavailable")]
    [InlineData("", "Unavailable")]
    [Scenario(TestScenario.Boundary)]
    public void ImportedSuspendingFunction_RecordedTraits_ChoosesTheCallForm(string traits, string expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenPrivateFrameRecord.cs")
                .Replace("TRAITS", traits, StringComparison.Ordinal));
        var compilation = DeviceLibraryBuilder.CreateConsumer(ConsumerOfTwiceOnly(), library);
        var callee = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation.GetTypeByMetadataName("XasmLib.LibMath"))
            .GetMembers("Twice")
            .OfType<IMethodSymbol>()
            .Single();
        var within = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("CrossAssemblyConsumer.ConsumerKernels"));

        var form = ManagedStateMachineAvailability.GetCallForm(callee, within, compilation);

        Assert.Equal(expected, form.ToString());
    }

    // A record written by the #1884 format (formatVersion: 3, before PrivateManagedFrame) differs from the current
    // format version and reports GSH0089 ("written by another version of G#"); a current record with
    // ManagedStateMachine = true, PrivateManagedFrame = true reports nothing on either dialect (#1991)
    [Theory]
    [InlineData("Cpu", 3, "", 1)]
    [InlineData("Cuda", 3, "", 1)]
    [InlineData("Cpu", 4, ", ManagedStateMachine = true, PrivateManagedFrame = true", 1)]
    [InlineData("Cuda", 4, ", ManagedStateMachine = true, PrivateManagedFrame = true", 1)]
    [InlineData("Cpu", 5, ", ManagedStateMachine = true, PrivateManagedFrame = true", 1)]
    [InlineData("Cuda", 5, ", ManagedStateMachine = true, PrivateManagedFrame = true", 1)]
    [InlineData("Cpu", 6, ", ManagedStateMachine = true, PrivateManagedFrame = true", 0)]
    [InlineData("Cuda", 6, ", ManagedStateMachine = true, PrivateManagedFrame = true", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task ImportedSuspendingFunction_RecordOfThePreviousFormat_ReportsGsh0089(
        string dialect,
        int formatVersion,
        string traits,
        int expected)
    {
        // formatVersion: 3 is the record written by the #1884 version; keep it at 3 across future format bumps so that
        // it continues to check that the previous format is rejected as another version (GSH0089)
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenPrivateFrameRecord.cs", formatVersion: formatVersion)
                .Replace("TRAITS", traits, StringComparison.Ordinal));

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(), library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id,
            expected > 0 ? "written by another version of G#" : null);
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
    }

    // A stack-only library function reports GSH0089 at a call that runs a state machine (the C++ dialect) unless both
    // CPU barrier models are Stack; a CUDA kernel calls its CUDA text
    [Theory]
    [InlineData("Cpu", null, null, 1)]
    [InlineData("Cuda", null, null, 0)]
    [InlineData("Cpu", "Stack", null, 1)]
    [InlineData("Cpu", null, "Stack", 1)]
    [InlineData("Cpu", "Stack", "Stack", 0)]
    [InlineData("Cuda", "Stack", "Stack", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task StackOnlyImportedFunction_ConsumerBarrierModels_ReportsGsh0089UnlessBothAreStack(
        string dialect,
        string? nativeModel,
        string? managedModel,
        int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenStackOnlyRecord.cs"));
        var options = new Dictionary<string, string>();
        if (nativeModel is not null)
        {
            options["build_property.GSharpNativeBarrierModel"] = nativeModel;
        }

        if (managedModel is not null)
        {
            options["build_property.GSharpManagedBarrierModel"] = managedModel;
        }

        var diagnostics = await RunAsync(dialect, ConsumerOfTwiceOnly(),
            options.Count > 0 ? options : null, library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id,
            "'LibMath.Twice' of 'XasmLib' runs only on the stack models; build the consumer with " +
            "GSharpNativeBarrierModel=Stack and GSharpManagedBarrierModel=Stack");
        Assert.Equal(expected, ranges.Count);
        Assert.All(ranges, static range => Assert.Equal((Analyzer, 11, 43, 11, 48), range));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "managed state machine"));
    }

    // The consumer without the struct use (the hand-written library records only Twice)
    private static string ConsumerOfTwiceOnly() =>
        s_consumer
            .Replace("var pair = new Pair(data[0]);", "var pair = data[0];          ", StringComparison.Ordinal)
            .Replace("data[1] = pair.A;", "data[1] = pair;  ", StringComparison.Ordinal);

    private static MetadataReference BuildHandWritten(string? preludeHash, bool cudaFailed, bool cppFailed) =>
        DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            HandWrittenExportRecords.Read(Folder + "HandWrittenRecord.cs", preludeHash: preludeHash)
                .Replace("CUDA_FAILED", cudaFailed ? "true" : "false", StringComparison.Ordinal)
                .Replace("CPP_FAILED", cppFailed ? "true" : "false", StringComparison.Ordinal));

    // #609: Counter() of a library has a body. Without a record the consumer cannot run it, so a written new Counter()
    // is GSH0088 at its new keyword and new T() with T = Counter, which the analyzer cannot see, is GSH0003 at the
    // creation (the written one is GSH0003 as well, as new Pair(1) is); with a record both call the imported constructor
    [Theory]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", false)]
    [InlineData("Cpu", true)]
    [InlineData("Cuda", true)]
    [Scenario(TestScenario.SadPath)]
    public async Task ParameterlessConstructorOfLibrary_UnrecordedConstructor_ReportsGsh0088(
        string dialect,
        bool exports)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", exports ? null : s_noExport,
            RoslynTestUtility.ReadTestCase(Folder + "ParameterlessLibrary.cs"));

        var (direct, directSource) = await RunWithGeneratedSourceAsync(dialect,
            RoslynTestUtility.ReadTestCase(Folder + "ParameterlessDirectConsumer.cs"), library);
        var (generic, genericSource) = await RunWithGeneratedSourceAsync(dialect,
            RoslynTestUtility.ReadTestCase(Folder + "ParameterlessConsumer.cs"), library);

        if (exports)
        {
            Assert.Empty(direct.Concat(generic).Where(static d => d.Id.StartsWith("GSH", StringComparison.Ordinal)));

            // Both creations call the library's constructor, which sets Value = 1, instead of Counter{}
            Assert.Contains("__gsharp_id_1_counter = __gsharp_ctor_XasmLib__Counter();", directSource,
                StringComparison.Ordinal);
            Assert.Contains("__gsharp_self.__gsharp_mb_Value = __gsharp_ctor_XasmLib__Counter();", genericSource,
                StringComparison.Ordinal);
            return;
        }

        // Line 11: var counter = new Counter(); line 9: public T Value = new T();
        Assert.Equal([(Analyzer, 11, 27, 11, 30)], direct.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id));
        Assert.Equal([(Analyzer, 11, 27, 11, 40)],
            direct.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "constructor 'XasmLib.Counter()' without a device equivalent"));
        Assert.Equal([(Analyzer, 9, 26, 9, 33)],
            generic.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "constructor 'XasmLib.Counter()' without a device equivalent"));
    }

    // #1278: explicit and implicit conversion operators, generic struct casts, and nullable lifted casts
    // of an imported struct without exported device records report GSH0088 at the conversion expression.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task LibraryWithoutRecords_ConversionOperator_ReportsGsh0088AtEachConversion(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_conversionLibrary);

        var diagnostics = await RunAsync(dialect, s_conversionConsumer, library);

        Assert.Equal([
            (Analyzer, 11, 22, 11, 49),
            (Analyzer, 12, 35, 12, 45),
            (Analyzer, 14, 24, 14, 34),
            (Analyzer, 15, 29, 15, 39),
            (Analyzer, 16, 24, 16, 25),
            (Analyzer, 19, 23, 19, 43),
            (Analyzer, 23, 22, 23, 45)
        ], diagnostics.Ranges(GSharpDiagnostics.DeviceFunctionNotExported.Id));
        Assert.Contains(diagnostics, static d =>
            d.Id == GSharpDiagnostics.DeviceFunctionNotExported.Id &&
            d.Message.Contains("'Meters.op_Explicit' is declared in 'XasmLib'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d =>
            d.Id == GSharpDiagnostics.DeviceFunctionNotExported.Id &&
            d.Message.Contains("'Meters.op_Implicit' is declared in 'XasmLib'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d =>
            d.Id == GSharpDiagnostics.DeviceFunctionNotExported.Id &&
            d.Message.Contains("'Box.op_Explicit' is declared in 'XasmLib'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task LibraryWithRecords_ConversionOperator_ReportsNothing(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", null, s_conversionLibrary);

        var diagnostics = await RunAsync(dialect, s_conversionConsumer, library);

        Assert.Empty(diagnostics.Where(static d =>
            d.Id is "GSH0088" or "GSH0089" or "GSH0090" &&
            d.Message.Contains("Meters", StringComparison.Ordinal)));
    }

    // #1278: a struct whose record failed for C++ (hand-written, CUDA text present): the CPU consumer reports GSH0089
    // at each conversion expression (line 11: intData[0], line 12: a), the CUDA consumer reports nothing
    [Theory]
    [InlineData("Cpu", 2)]
    [InlineData("Cuda", 0)]
    [Scenario(TestScenario.SadPath)]
    public async Task LibraryWithUnusableRecord_ConversionOperator_ReportsGsh0089AtEachConversion(
        string dialect,
        int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport,
            HandWrittenExportRecords.Read(Folder + "HandWrittenConversionRecord.cs"));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "ConversionUnusableConsumer.cs"),
            library);

        var ranges = diagnostics.Ranges(GSharpDiagnostics.DeviceExportUnusable.Id, "could not transpile it");
        Assert.Equal(expected == 0 ? [] : [(Analyzer, 11, 24, 11, 34), (Analyzer, 12, 24, 12, 25)], ranges);
        Assert.Empty(diagnostics.Where(static d => d.Id is "GSH0088" or "GSH0090"));
    }

    // Partial [GpuDeviceFunction] export divergence matches between PartialDefinitionPart and
    // PartialImplementationPart, and consumer assemblies diagnose variant arguments while accepting uniform ones
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PartialDeviceFunction_GetExportedDivergence_MatchesBetweenDefinitionAndImplementation()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace PartialLib;

            public static partial class PartialFunctions
            {
                [GpuDeviceFunction]
                public static partial int PartialCondSyncAttrOnDef(int x);

                public static partial int PartialCondSyncAttrOnDef(int x)
                {
                    if (x > 0)
                    {
                        Gpu.SyncThreads();
                    }

                    return x;
                }

                [GpuDeviceFunction]
                public static partial int PartialCondTwoBarriersAttrOnDef(int x);

                public static partial int PartialCondTwoBarriersAttrOnDef(int x)
                {
                    if (x > 0)
                    {
                        Gpu.SyncThreads();
                    }

                    Gpu.SyncThreads();
                    return x;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("PartialLib.PartialFunctions"));

        var condSyncDef = type.GetMembers("PartialCondSyncAttrOnDef").OfType<IMethodSymbol>().Single();
        var condSyncImpl = Assert.IsAssignableFrom<IMethodSymbol>(condSyncDef.PartialImplementationPart);
        var expectedCondSync = new ExportedBarrierDivergence(0, 1, false);
        Assert.Equal(expectedCondSync, UniformityAnalysis.GetExportedDivergence(condSyncDef, compilation));
        Assert.Equal(expectedCondSync, UniformityAnalysis.GetExportedDivergence(condSyncImpl, compilation));

        var twoBarriersDef = type.GetMembers("PartialCondTwoBarriersAttrOnDef").OfType<IMethodSymbol>().Single();
        var twoBarriersImpl = Assert.IsAssignableFrom<IMethodSymbol>(twoBarriersDef.PartialImplementationPart);
        var expectedTwoBarriers = new ExportedBarrierDivergence(1, 0, false);
        Assert.Equal(expectedTwoBarriers, UniformityAnalysis.GetExportedDivergence(twoBarriersDef, compilation));
        Assert.Equal(expectedTwoBarriers, UniformityAnalysis.GetExportedDivergence(twoBarriersImpl, compilation));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ImportedPartialDeviceFunction_CrossAssemblyCall_DiagnosesVariantAndAcceptsUniform(
        string dialect)
    {
        const string LibrarySource = """
            using Kassyi.GSharp.Core;

            namespace PartialLib;

            public static partial class PartialFunctions
            {
                [GpuDeviceFunction]
                public static partial int PartialCondSyncAttrOnDef(int x);

                public static partial int PartialCondSyncAttrOnDef(int x)
                {
                    if (x > 0)
                    {
                        Gpu.SyncThreads();
                    }

                    return x;
                }

                [GpuDeviceFunction]
                public static partial int PartialCondTwoBarriersAttrOnDef(int x);

                public static partial int PartialCondTwoBarriersAttrOnDef(int x)
                {
                    if (x > 0)
                    {
                        Gpu.SyncThreads();
                    }

                    Gpu.SyncThreads();
                    return x;
                }
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;
            using PartialLib;

            namespace PartialConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void ConsumeUniformPartialCond(int[] o)
                {
                    o[0] = PartialFunctions.PartialCondSyncAttrOnDef(1);
                    Gpu.SyncThreads();
                }

                [GpuKernel]
                public static void ConsumeVariantPartialTwoBarriersDef(int[] o)
                {
                    o[0] = PartialFunctions.PartialCondTwoBarriersAttrOnDef(ThreadIndex.X);
                }
            }
            """;

        var library = DeviceLibraryBuilder.Build("PartialLib", null, LibrarySource);

        var diagnostics = await RunAsync(dialect, ConsumerSource, library);

        Assert.Equal([(Analyzer, 18, 16, 18, 79)],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.BarrierAfterDivergentExit.Id));
    }

    // The diagnostics of a run and the text of the sources the generator added
    private static async Task<(ImmutableArray<ReportedDiagnostic> Diagnostics, string Generated)>
        RunWithGeneratedSourceAsync(string dialect, string consumer, params MetadataReference[] libraries)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(DeviceLibraryBuilder.CreateConsumer(consumer, libraries),
            dialect);
        var generated = string.Concat(run.Output.SyntaxTrees
            .Where(tree => !run.Input.SyntaxTrees.Contains(tree))
            .Select(static tree => tree.ToString()));
        var diagnostics = run.Analyzer
            .Select(static d => ReportedDiagnostic.From(Analyzer, d))
            .Concat(run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .ToImmutableArray();
        return (diagnostics, generated);
    }

    private static async Task<ImmutableArray<ReportedDiagnostic>> RunAsync(string dialect, string consumer,
        Dictionary<string, string>? options, params MetadataReference[] libraries)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(DeviceLibraryBuilder.CreateConsumer(consumer, libraries),
            dialect, options);
        return run.Analyzer
            .Select(static d => ReportedDiagnostic.From(Analyzer, d))
            .Concat(run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .ToImmutableArray();
    }

    private static Task<ImmutableArray<ReportedDiagnostic>> RunAsync(string dialect, string consumer,
        params MetadataReference[] libraries) =>
        RunAsync(dialect, consumer, options: null, libraries);
}
