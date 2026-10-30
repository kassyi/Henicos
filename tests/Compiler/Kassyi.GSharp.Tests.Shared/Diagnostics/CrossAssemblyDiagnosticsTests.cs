#nullable enable
using Kassyi.GSharp.Compiler.Models;
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

    private static readonly Dictionary<string, string> s_noExport =
        new() { ["build_property.GSharpExportDeviceCode"] = "false" };

    // Line 11: LibMath.Twice(...), the name of the call; line 12: new Pair(...), the new keyword of the creation
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LibraryWithoutRecords_ReportsGsh0088AtEachUse(string dialect)
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
    public async Task Test_LibraryWithRecords_ReportsNothing(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", null, s_library);

        var diagnostics = await RunAsync(dialect, s_consumer, library);

        Assert.Empty(diagnostics.Where(static d => d.Id is "GSH0088" or "GSH0089" or "GSH0090"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RecordOfAnotherPrelude_ReportsGsh0089(string dialect)
    {
        var library = BuildHandWritten("0123456789abcdef", cudaFailed: false, cppFailed: false);

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
    public async Task Test_RecordThatFailedInTheDialect_ReportsGsh0089(string dialect, bool cudaFailed, bool cppFailed,
        int expected)
    {
        var library = BuildHandWritten(DeviceExportRecord.CurrentPreludeHash, cudaFailed, cppFailed);

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
    public async Task Test_TwoLibrariesWithOnePath_ReportsGsh0090(string dialect)
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
    public async Task Test_TypeWithThePathOfTwoLibraries_ReportsGsh0090(string dialect)
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
    public async Task Test_GenericTypeAndFieldWithThePathOfTwoLibraries_ReportGsh0090(string dialect)
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
    public async Task Test_RecordWithAnInternalError_ReportsGsh0089WithTheMessage(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenInternalError.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

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
    public async Task Test_UsingOfAStructThatFailedInTheDialect_ReportsGsh0089(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenLease.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

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
    public async Task Test_SuspendingCallFromAnExportedDeviceFunction_ReportsGsh0089(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenSuspendingRecord.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

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
    public async Task Test_SuspendingFunctionWithoutManagedStateMachine_ReportsGsh0089(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenSuspendingRecord.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

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
    public async Task Test_TargetTypedNew_ReportsGsh0088(string dialect)
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
    public async Task Test_DependencyThatFailedInTheDialect_ReportsGsh0089AtTheUse(string dialect, int expected)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenChain.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

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
    public async Task Test_DependencyInAThirdAssembly_ReportsGsh0088UnlessItIsExported(string dialect, bool innerExports)
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
    public async Task Test_DependencyWithThePathOfAnotherLibrary_ReportsGsh0090AtTheUse(string dialect)
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
    public async Task Test_DependencyWithThePathOfTheConsumer_ReportsGsh0090AtTheUse(string dialect)
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
    public async Task Test_GenericIteratorAndGridFunctions_ReportedOnce(string dialect)
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
    public async Task Test_ImportedDivergentParameter_ReportsGsh0083(string dialect)
    {
        var library = DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenDivergentRecord.cs")
                .Replace("PRELUDE_HASH", DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal));

        var diagnostics = await RunAsync(dialect, RoslynTestUtility.ReadTestCase(Folder + "DivergentCallConsumer.cs"),
            library);

        Assert.Equal([(Analyzer, 11, 23, 11, 51)],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
    }

    // The consumer without the struct use (the hand-written library records only Twice)
    private static string ConsumerOfTwiceOnly() =>
        s_consumer
            .Replace("var pair = new Pair(data[0]);", "var pair = data[0];          ", StringComparison.Ordinal)
            .Replace("data[1] = pair.A;", "data[1] = pair;  ", StringComparison.Ordinal);

    private static MetadataReference BuildHandWritten(string preludeHash, bool cudaFailed, bool cppFailed) =>
        DeviceLibraryBuilder.Build("XasmLib", s_noExport, s_library,
            RoslynTestUtility.ReadTestCase(Folder + "HandWrittenRecord.cs")
                .Replace("PRELUDE_HASH", preludeHash, StringComparison.Ordinal)
                .Replace("CUDA_FAILED", cudaFailed ? "true" : "false", StringComparison.Ordinal)
                .Replace("CPP_FAILED", cppFailed ? "true" : "false", StringComparison.Ordinal));

    private static async Task<ImmutableArray<ReportedDiagnostic>> RunAsync(string dialect, string consumer,
        params MetadataReference[] libraries)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(DeviceLibraryBuilder.CreateConsumer(consumer, libraries),
            dialect);
        return run.Analyzer
            .Select(static d => ReportedDiagnostic.From(Analyzer, d))
            .Concat(run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .ToImmutableArray();
    }
}
