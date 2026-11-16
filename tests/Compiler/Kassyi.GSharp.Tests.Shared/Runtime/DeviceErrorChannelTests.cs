using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.ErrorSites;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Naming;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies the device runtime error channel and CPU/GPU exception parity (#729).
/// </summary>
public partial class DeviceErrorChannelTests
{
    public static TheoryData<GSharpErrorCode, Type> AllErrorCodesAndExceptionTypes() =>
        new()
        {
            { GSharpErrorCode.NotSupported, typeof(NotSupportedException) },
            { GSharpErrorCode.InvalidOperation, typeof(InvalidOperationException) },
            { GSharpErrorCode.IndexOutOfRange, typeof(IndexOutOfRangeException) },
            { GSharpErrorCode.ArgumentOutOfRange, typeof(ArgumentOutOfRangeException) },
            { GSharpErrorCode.Argument, typeof(ArgumentException) },
            { GSharpErrorCode.NullReference, typeof(NullReferenceException) },
            { GSharpErrorCode.DivideByZero, typeof(DivideByZeroException) },
            { GSharpErrorCode.Overflow, typeof(OverflowException) },
            { GSharpErrorCode.DataRace, typeof(InvalidOperationException) },
            { GSharpErrorCode.SwitchExpression, typeof(SwitchExpressionException) }
        };

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(AllErrorCodesAndExceptionTypes))]
    public void ErrorMapper_AllErrorCodes_MapsToStandardExceptionsWithMetadata(
        GSharpErrorCode code,
        Type expectedExceptionType)
    {
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            $"test-module-{code}",
            [
                new GSharpErrorSite(1u, "MyKernels.cu.cs", 42, 13, "MyKernel", $"Custom error for {code}")
            ]);

        var packedThread = GSharpErrorMapper.PackThreadIndex(7, 3, 1);
        var innerCuda = new CudaException(CUresult.CudaErrorIllegalInstruction);

        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)code | GSharpErrorMapper.CompletionBit,
            SiteId = 1u,
            ModuleId = moduleId,
            ThreadLinear = packedThread,
            BlockX = 2u,
            BlockYZ = 4u | (1u << 16)
        };

        var ex = GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerCuda);

        Assert.IsType(expectedExceptionType, ex);
        Assert.Same(innerCuda, ex.InnerException);
        Assert.Contains($"Custom error for {code} in MyKernel at MyKernels.cu.cs(42,13) in thread (7,3,1) of block (2,4,1)", ex.Message);
        Assert.Equal(code.ToString(), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("MyKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("MyKernels.cu.cs(42,13)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(7,3,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(2,4,1)", ex.Data["GSharp.BlockIndex"]);
        Assert.Equal(nameof(CUresult.CudaErrorIllegalInstruction), ex.Data["GSharp.CudaError"]);
    }

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(GSharpErrorCode.ArgumentNull, typeof(ArgumentNullException), "Value cannot be null.", "array")]
    [InlineData(GSharpErrorCode.Argument, typeof(ArgumentException), "Destination is too short.", "destination")]
    [InlineData(GSharpErrorCode.ArgumentOutOfRange, typeof(ArgumentOutOfRangeException), "Count is out.", "count")]
    public void ErrorMapper_ArgumentErrorNamingAParameter_SetsParamNameOnceInTheMessage(
        GSharpErrorCode code,
        Type expectedExceptionType,
        string text,
        string parameter)
    {
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            $"test-module-param-{code}",
            [new GSharpErrorSite(2u, "MyKernels.cu.cs", 7, 3, "MyKernel", $"{text} (Parameter '{parameter}')")]);
        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)code | GSharpErrorMapper.CompletionBit,
            SiteId = 2u,
            ModuleId = moduleId,
        };

        var ex = Assert.IsAssignableFrom<ArgumentException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot));

        Assert.IsType(expectedExceptionType, ex);
        Assert.Equal(parameter, ex.ParamName);
        Assert.StartsWith($"{text} in MyKernel at MyKernels.cu.cs(7,3)", ex.Message);
        Assert.EndsWith($"(Parameter '{parameter}')", ex.Message);
        Assert.Equal(
            ex.Message.IndexOf("(Parameter '", StringComparison.Ordinal),
            ex.Message.LastIndexOf("(Parameter '", StringComparison.Ordinal));
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void ErrorMapper_EveryArgumentExceptionMessageOfTheCompiler_IsAKnownBuiltinMessage()
    {
        foreach (var message in ArgumentExceptionMessages.All)
        {
            var slot = new GSharpDeviceErrorSlot
            {
                Code = (uint)GSharpErrorCode.ArgumentOutOfRange | GSharpErrorMapper.CompletionBit,
                SiteId = GSharpErrorMapper.ComputeBuiltinMessageId(message),
            };

            var ex = Assert.IsType<ArgumentOutOfRangeException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot));

            Assert.NotNull(ex.ParamName);
            Assert.Contains(ex.ParamName, message);
        }
    }

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Resources/Prelude.cu")]
    [InlineData("Resources/Prelude/Types.hpp")]
    [InlineData("Resources/StackLinq.cuh")]
    public void ErrorMapper_EveryParameterMessageOfThePrelude_IsAKnownBuiltinMessage(string preludeFile)
    {
        // C++ text has no syntax tree: the literals of the prelude that name a parameter are read from the file
        var text = File.ReadAllText(
            RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", preludeFile));
#pragma warning disable RS0030 // C++ text of the prelude has no syntax tree
        var literals = Regex.Matches(text, @"""((?:[^""\\]|\\.)*\(Parameter '[^']*'\))""");
#pragma warning restore RS0030
        Assert.NotEmpty(literals);

        foreach (var literal in literals.Select(static m => m.Groups[1].Value))
        {
            var slot = new GSharpDeviceErrorSlot
            {
                Code = (uint)GSharpErrorCode.ArgumentOutOfRange | GSharpErrorMapper.CompletionBit,
                SiteId = GSharpErrorMapper.ComputeBuiltinMessageId(literal),
            };

            var ex = Assert.IsType<ArgumentOutOfRangeException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot));

            Assert.True(ex.ParamName is not null, $"The mapper does not know \"{literal}\" of {preludeFile}");
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ErrorMapper_ArgumentErrorWithoutAParameter_HasNoParamName()
    {
        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)GSharpErrorCode.ArgumentOutOfRange | GSharpErrorMapper.CompletionBit,
        };

        var ex = Assert.IsType<ArgumentOutOfRangeException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot));

        Assert.Null(ex.ParamName);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ErrorMapper_MessageEndingInAnEmptyParameterName_KeepsItsText()
    {
        // new ArgumentException(message, "") drops the suffix, so an empty name must not be taken from the text
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            "test-module-empty-param-name",
            [new GSharpErrorSite(3u, "MyKernels.cu.cs", 9, 1, "MyKernel", "Destination is too short. (Parameter '')")]);
        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)GSharpErrorCode.Argument | GSharpErrorMapper.CompletionBit,
            SiteId = 3u,
            ModuleId = moduleId,
        };

        var ex = Assert.IsType<ArgumentException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot));

        Assert.Null(ex.ParamName);
        Assert.Contains("(Parameter '')", ex.Message);
    }

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(GSharpErrorCode.Argument, true)]
    [InlineData(GSharpErrorCode.ArgumentOutOfRange, false)]
    [InlineData(GSharpErrorCode.ArgumentNull, false)]
    public void ErrorMapper_ArgumentErrorWithAnInnerException_KeepsParamNameOnlyWhereTheConstructorTakesBoth(
        GSharpErrorCode code,
        bool keepsParamName)
    {
        // ArgumentException(message, paramName, innerException) exists; the other two types have no such constructor
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            $"test-module-param-inner-{code}",
            [new GSharpErrorSite(4u, "MyKernels.cu.cs", 11, 5, "MyKernel", "Bad value. (Parameter 'count')")]);
        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)code | GSharpErrorMapper.CompletionBit,
            SiteId = 4u,
            ModuleId = moduleId,
        };
        var inner = new InvalidOperationException("inner");

        var ex = Assert.IsAssignableFrom<ArgumentException>(GSharpErrorMapper.CreateExceptionFromSlot(in slot, inner));

        Assert.Equal(keepsParamName ? "count" : null, ex.ParamName);
        Assert.Contains("(Parameter 'count')", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void CoordinatePacking_ThreeDimensionalCoordinates_PacksAndUnpacksCoordinates()
    {
        var packedThread = GSharpErrorMapper.PackThreadIndex(511, 15, 3);
        Assert.Equal((511, 15, 3), GSharpErrorMapper.UnpackThreadIndex(packedThread));

        Assert.Equal((4, 9, 3), GSharpErrorMapper.UnpackBlockIndex(4u, 9u | (3u << 16)));
        Assert.Equal((int.MaxValue, 65535, 65535), GSharpErrorMapper.UnpackBlockIndex(0x7FFFFFFFu, 0xFFFFFFFFu));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void ErrorSiteTable_SymbolRegistration_DeduplicatesAndRoundTripsSymbolFile()
    {
        var table = new ErrorSiteTable();
        var id1 = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");
        var id2 = table.Register("Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array.");
        var id1Duplicate = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");

        Assert.True(id1 != 0u);
        Assert.True(id2 != 0u);
        Assert.NotEqual(id1, id2);
        Assert.Equal(id1, id1Duplicate);
        Assert.Equal(2, table.Count);

        using var writer = new SourceWriter();
        ErrorSiteTable.AppendTrapSiteCall(writer, "DivideByZero", id1);
        Assert.Equal($"__gsharp_trap_site(GSharpErrorCode::DivideByZero, {id1}u)", writer.ToString());

        var gsymRecords = new List<GSharpNameEncoding.SymbolErrorSiteRecord>();
        foreach (var entry in table.Entries)
        {
            gsymRecords.Add(new GSharpNameEncoding.SymbolErrorSiteRecord(
                entry.SiteId,
                entry.File,
                entry.Line,
                entry.Column,
                entry.MemberName,
                entry.DefaultMessage));
        }

        const string bodySource = "extern \"C\" __global__ void __gsharp_k_SampleKernel() {}\n";
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(bodySource, "Cuda", errorSites: gsymRecords);
        var expectedHash = GSharpNameEncoding.ComputeSourceHash(bodySource);
        var parsed = GSharpNameEncoding.ParseErrorSitesFromSymbolFile(gsymContent, expectedHash);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(id1, "Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero."), parsed[id1]);
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(id2, "Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array."), parsed[id2]);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void ErrorSiteTable_BuildModuleSites_DetectsCollisionAndDeduplicates()
    {
        // 1. Deduplication of identical entries
        var entry1 = new ErrorSiteEntry(100u, "Test.cs", 10, 5, "Kernel1", "Error 1");
        var entry1Dup = new ErrorSiteEntry(100u, "Test.cs", 10, 5, "Kernel1", "Error 1");
        var entry2 = new ErrorSiteEntry(200u, "Test.cs", 20, 5, "Kernel2", "Error 2");

        var success = ErrorSiteTable.TryBuildModuleSites(
            [entry1, entry1Dup, entry2],
            out var uniqueSites,
            out var c1,
            out var c2);

        Assert.True(success);
        Assert.Equal(2, uniqueSites.Length);
        Assert.Equal(100u, uniqueSites[0].SiteId);
        Assert.Equal(200u, uniqueSites[1].SiteId);

        // 2. Collision detection when same site ID has different properties
        var entryConflict = new ErrorSiteEntry(100u, "Other.cs", 30, 1, "KernelOther", "Conflicting error");
        var collisionDetected = !ErrorSiteTable.TryBuildModuleSites(
            [entry1, entryConflict],
            out _,
            out var first,
            out var second);

        Assert.True(collisionDetected);
        Assert.Equal(100u, first.SiteId);
        Assert.Equal("Kernel1", first.MemberName);
        Assert.Equal("KernelOther", second.MemberName);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void ErrorSiteTable_VaryingLineEndings_ProducesIdenticalSiteIds()
    {
#pragma warning disable RS0030 // The line endings of the source text are the subject: the same file with LF and with CRLF
        var source = RoslynTestUtility.ReadTestCase("Runtime/ErrorSiteLineEndingCases.cs").Replace("\r\n", "\n");
#pragma warning restore RS0030

        var lf = CreateSitesForTerminals(source);
#pragma warning disable RS0030 // The line endings of the source text are the subject: the same file with LF and with CRLF
        var crlf = CreateSitesForTerminals(source.Replace("\n", "\r\n"));
#pragma warning restore RS0030

        // Three terminals on different lines, each with a real file name, so the comparison below cannot pass vacuously
        Assert.Equal(3, lf.Count);
        Assert.All(lf, static site => Assert.Equal("ErrorSiteLineEndingCases.cs", site.File));
        Assert.Equal(lf.Select(static site => site.SiteId).Distinct().Count(), lf.Count);
        Assert.Equal(lf, crlf);
    }

    private static List<ErrorSiteEntry> CreateSitesForTerminals(string source)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt("ErrorSiteLineEndingCases.cs", source)
            .WithAssemblyName("ErrorSiteLineEndings");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Where(static call => call.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ElementAt" or "First" or "Last"
            })
            .Select(call => ErrorSiteTable.CreateSite(
                call,
                model,
                "SitesOnSeveralLinesKernel",
                "Sequence contains no elements",
                ((Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText)
            ).ToList();
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedBuildOption_CheckedAndUncheckedConfigurations_ControlsPostLaunchStreamSynchronization(
        bool isCheckedBuild)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace SampleNamespace;

            public static partial class SampleKernels
            {
                [GpuKernel]
                public static void VoidScalarKernel(int value)
                {
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source)
            .WithAssemblyName("SampleAssembly");

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            "Cuda",
            new Dictionary<string, string>
            {
                ["build_property.GSharpCheckedBuild"] = isCheckedBuild ? "true" : "false"
            });

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            optionsProvider: optionsProvider);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var wrapperTree = Assert.Single(runResult.GeneratedTrees, t => t.FilePath.EndsWith("SampleKernels.g.cs", StringComparison.Ordinal));
        var wrapperText = wrapperTree.GetText().ToString();

        // The checked build synchronizes after both launches. In the release build only the managed-array launcher does,
        // since this kernel has no copy-back that would synchronize, so that it reads its own record; the Ptr launcher
        // leaves the error to the next synchronization of the stream.
        const string Synchronize = "cuStreamSynchronize(hStream));";
        Assert.Equal(isCheckedBuild ? 2 : 1, wrapperText.Split(Synchronize).Length - 1);

        // The managed-array launcher reads the device error record of its own launch in every build; the Ptr launcher
        // only where it synchronizes, in the checked build. Both take an epoch for the launch and pass it last; a
        // launcher that reads its record owns the epoch until it does, and releases it in its finally block.
        const string Records = "global::Kassyi.GSharp.Runtime.Aot.LaunchErrorRecords.";
        int readers = isCheckedBuild ? 2 : 1;
        Assert.Equal(readers, Count(wrapperText, $"{Records}ThrowIfLaunchError(__gsharp_epoch);"));
        Assert.Equal(readers, Count(wrapperText, $"{Records}ReleaseLaunchEpoch(__gsharp_epoch);"));
        Assert.DoesNotContain("ThrowIfDeviceError", wrapperText);
        Assert.Equal(readers, Count(wrapperText, $"uint __gsharp_epoch = {Records}NextLaunchEpoch(hStream, true);"));
        Assert.Equal(2 - readers,
            Count(wrapperText, $"uint __gsharp_epoch = {Records}NextLaunchEpoch(hStream, false);"));

        static int Count(string text, string part) => text.Split(part).Length - 1;
    }
}
