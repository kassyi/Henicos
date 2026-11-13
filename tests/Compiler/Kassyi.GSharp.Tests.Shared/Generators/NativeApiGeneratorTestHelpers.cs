#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generators;

internal static class NativeApiGeneratorTestHelpers
{
    internal const string LibraryYaml = """
        namespace: MyApp.Interop
        className: MyBlas
        libraryName: myblas
        libraryNames:
          windows: [myblas64_2.dll, "%MYBLAS_HOME%/bin/myblas64_2.dll"]
          linux: [libmyblas.so.2]
        methods:
          - name: myblasCreate
            summary: Creates a <handle> & context.
            returnType: int
            returnDescription: Zero on success.
            parameters:
              - name: handle
                type: out IntPtr
                description: Receives the created handle.
          - name: myblasSgemm_v2
            entryPoint: myblasSgemm
            isUnsafe: true
            returnType: int
            parameters:
              - { name: handle, type: IntPtr }
              - { name: alpha, type: float* }
        """;

    internal const string ModuleYaml = """
        namespace: MyApp.Native
        className: MyKernels
        moduleName: ./native/my-custom.kernels.fatbin
        methods:
          - name: SgemmCustom
            entryPoint: sgemm_kernel_v1
            summary: User-defined custom matrix multiplication kernel.
            parameters:
              - { name: a, type: "float[]", description: Matrix A. }
              - { name: c, type: "float[]", description: Matrix C. }
              - { name: n, type: int }
              - { name: gridDimX, type: uint }
              - { name: blockDimX, type: uint }
          - name: Fill
            parameters:
              - { name: data, type: "int[]" }
              - { name: value, type: int }
        """;

    internal sealed record TestFile(string Path, string Text, bool IsGSharpApi);

    internal static TestFile Api(string path, string text) => new(path, text, IsGSharpApi: true);

    internal static GeneratorDriverRunResult RunGenerator(params TestFile[] files)
    {
        var driver = CreateDriver(files).RunGenerators(CreateCompilation());
        return driver.GetRunResult();
    }

    internal static GeneratorDriver CreateDriver(params TestFile[] files) =>
        CreateDriver([new NativeApiGenerator().AsSourceGenerator()], new CSharpParseOptions(LanguageVersion.Latest),
            files);

    /// <summary>
    /// A driver that runs the YAML bindings and the <c>[CudaImport]</c> launchers together on
    /// <paramref name="compilation"/>, parsing their output as its sources are parsed.
    /// </summary>
    internal static GeneratorDriver CreateDriverWithCudaImport(Compilation compilation, params TestFile[] files) =>
        CreateDriver([new NativeApiGenerator().AsSourceGenerator(), new CudaImportGenerator().AsSourceGenerator()],
            (CSharpParseOptions)compilation.SyntaxTrees.First().Options, files);

    /// <summary>The errors of <paramref name="compilation"/>, one per line, for an assertion message.</summary>
    internal static string[] GetErrors(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

    private static GeneratorDriver CreateDriver(ISourceGenerator[] generators, CSharpParseOptions parseOptions,
        TestFile[] files)
    {
        var additionalTexts = files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToArray();
        var apiPaths = files.Where(f => f.IsGSharpApi).Select(f => f.Path).ToHashSet();

        return CSharpGeneratorDriver.Create(
            generators: generators,
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: new PerFileOptionsProvider(apiPaths),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    /// <summary>The assembly name of <see cref="CreateCompilation"/>, for a compilation built another way.</summary>
    internal const string AssemblyName = "NativeApiGeneratorTestAssembly";

    // The shared references hold the trusted platform assemblies (Kassyi.GSharp.Runtime with GpuKernelLoader among
    // them) once per process; creating them per test read every image into memory again.
    internal static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            AssemblyName,
            [],
            RoslynTestUtility.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    internal static string FindSource(GeneratorDriverRunResult result, string marker)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.Contains(marker))
            {
                return tree.ToString();
            }
        }

        throw new Xunit.Sdk.XunitException($"No generated source whose hint name contains '{marker}'.");
    }

    /// <summary>
    /// Verifies each generated file as its own snapshot so that every <c>.verified.cs</c> stays valid C#.
    /// </summary>
    internal static async Task VerifyGenerated(GeneratorDriverRunResult result, string testName)
    {
        Assert.NotEmpty(result.GeneratedTrees);
        foreach (var tree in result.GeneratedTrees.OrderBy(t => Path.GetFileName(t.FilePath), StringComparer.Ordinal))
        {
            var hintName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(tree.FilePath));
            await Verifier.Verify(tree.ToString(), "cs")
                .UseDirectory("Snapshots")
                .UseFileName($"Snap.{testName}#{hintName}");
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed class PerFileOptionsProvider(HashSet<string> apiPaths) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_api = new DictionaryOptions(true);
        private static readonly AnalyzerConfigOptions s_none = new DictionaryOptions(false);

        public override AnalyzerConfigOptions GlobalOptions => s_none;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_none;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            apiPaths.Contains(textFile.Path) ? s_api : s_none;
    }

    private sealed class DictionaryOptions(bool isApi) : AnalyzerConfigOptions
    {
#pragma warning disable CS8765 // NotNullWhen is ambiguous with the PolySharp copy inside Kassyi.GSharp.Compiler
        public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
        {
            if (isApi && key == NativeApiGenerator.GSharpApiMetadataKey)
            {
                value = "true";
                return true;
            }

            value = null;
            return false;
        }
    }
}
