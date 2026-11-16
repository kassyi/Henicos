#nullable enable
using System.Globalization;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// GCNR0001: transpiles every kernel and device function in the shared test sources on both dialects and fails when a
/// user-declared parameter, local, label or local function reaches the generated code without going through
/// <c>CppNameResolver</c>.
/// </summary>
/// <remarks>
/// <para>
/// A name written past the resolver leaves no record, so the test finds it by renaming: every user name declared in a
/// GPU method is renamed at the symbol level to a unique probe name (<see cref="UserNameProbeRenamer"/>) before the
/// transpilation. Each probe in the output must be the end of the spelling that <see cref="CppNameResolver"/> gives its
/// symbol: <c>__gsharp_id_{ordinal}_</c>, or <c>__gsharp_il_{region}_{ordinal}_</c> in a copy that an expansion
/// stamped with a region. Any other text in front of it, a compiler prefix such as <c>__gsharp_in_</c> included, means
/// that an emission site wrote the name itself.
/// </para>
/// <para>
/// A <c>nameof</c> keeps the original name as its value (the renamer replaces it with its string literal), so its text
/// never carries a probe. String literals and comments of the source are not renamed either.
/// </para>
/// <para>
/// GCNR is the test-only family for internal consistency checks; it never appears in <c>diagnostics.json</c>.
/// </para>
/// </remarks>
[Collection(TestCollections.CorpusScan)]
public class GeneratedNameLeakTests
{
    private const string LeakId = "GCNR0001";

    private static readonly ITranspilerDialect[] s_dialects = [new GSharpCudaDialect(), new StandardCppDialect()];

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void GeneratedCode_TranspilerEmission_DoesNotLeakRawUserIdentifiers()
    {
        var renamer = UserNameProbeRenamer.Rename(RoslynTestUtility.SharedCompilation, IsGpuMethod);
        var leaks = new List<string>();
        var transpiled = 0;
        var resolved = 0;

        foreach (var (method, dialect, source) in TranspileGpuMethods(renamer))
        {
            transpiled++;
            foreach (var (number, start) in FindProbes(source))
            {
                var symbol = renamer.GetSymbol(number);
                if (EndsResolverSpelling(source, start, symbol, dialect))
                {
                    resolved++;
                    continue;
                }

                leaks.Add($"{method.ContainingType.Name}.{method.Name} ({dialect.GetType().Name}): {symbol.Name}");
            }
        }

        Assert.True(renamer.AddedErrors.IsEmpty,
            "The rename left references that no longer bind:" + Environment.NewLine +
            string.Join(Environment.NewLine, renamer.AddedErrors));
        Assert.True(renamer.ProbeCount > 0, "No user name was renamed; the scan did not run.");
        Assert.True(transpiled > 0, "No GPU method was transpiled; the scan did not run.");
        Assert.True(resolved > 0, "No renamed user name reached the generated code through the resolver.");
        Assert.True(leaks.Count == 0,
            $"{LeakId}: raw user identifiers in generated code:" + Environment.NewLine +
            string.Join(Environment.NewLine, leaks.Distinct()));
    }

    // The C++ of every GPU method of the rewritten trees that transpiles without a failure, on both dialects
    private static IEnumerable<(IMethodSymbol Method, ITranspilerDialect Dialect, string Source)> TranspileGpuMethods(
        UserNameProbeRenamer renamer)
    {
        foreach (var (declaration, model, method) in GpuMethods(renamer))
        {
            foreach (var dialect in s_dialects)
            {
                if (Transpile(declaration, model, method, dialect) is { } source)
                {
                    yield return (method, dialect, source);
                }
            }
        }
    }

    private static IEnumerable<(MethodDeclarationSyntax Declaration, SemanticModel Model, IMethodSymbol Method)>
        GpuMethods(UserNameProbeRenamer renamer) =>
        renamer.RewrittenTrees
            .Select(tree => renamer.Compilation.GetSemanticModel(tree))
            .SelectMany(static model => model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Select(declaration => (declaration, model, method: model.GetDeclaredSymbol(declaration)))
            ).Where(static m => m.method != null && IsGpuMethod(m.method))
            .Select(static m => (m.declaration, m.model, m.method ?? throw new InvalidOperationException()));

    private static string? Transpile(MethodDeclarationSyntax declaration, SemanticModel model, IMethodSymbol method,
        ITranspilerDialect dialect)
    {
        var failures = new List<TranspileFailure>();
        var isDevice = !method.HasAttribute(KnownAttributes.GpuKernel);
        var source = MethodTranspiler.GenerateCudaSource(declaration, model, failures.Add, isDevice, dialect, out _);
        return failures.Count == 0 ? source : null;
    }

    private static bool IsGpuMethod(IMethodSymbol symbol) =>
        symbol.HasAttribute(KnownAttributes.GpuKernel) || symbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

    // Every probe in the generated code: its number and where it starts
    private static IEnumerable<(int Number, int Start)> FindProbes(string source)
    {
        var start = source.IndexOf(UserNameProbeRenamer.Stem, StringComparison.Ordinal);
        while (start >= 0)
        {
            var digits = start + UserNameProbeRenamer.Stem.Length;
            var end = source.IndexOf(UserNameProbeRenamer.Terminator, digits);
            yield return (int.Parse(source.AsSpan(digits, end - digits), CultureInfo.InvariantCulture), start);
            start = source.IndexOf(UserNameProbeRenamer.Stem, end, StringComparison.Ordinal);
        }
    }

    // Whether the text in front of the probe at probeStart is what the resolver writes in front of the name of symbol:
    // the spelling without a region, or the one with the region that the text names after the inlined prefix
    private static bool EndsResolverSpelling(string source, int probeStart, ISymbol symbol, ITranspilerDialect dialect)
    {
        var name = CppNameResolver.Resolve(symbol, symbol.Name, dialect);
        if (name.Kind != CppNameKind.FunctionScope)
        {
            return false;
        }

        if (EndsWith(source, probeStart, SpellingBeforeName(name.Ordinal, region: 0)))
        {
            return true;
        }

        var prefix = GSharpInternalNames.Renamed.InlinedIdentifierPrefix;
        var prefixStart = source.LastIndexOf(prefix, probeStart, StringComparison.Ordinal);
        if (prefixStart < 0)
        {
            return false;
        }

        var regionStart = prefixStart + prefix.Length;
        var regionEnd = source.IndexOf('_', regionStart);
        return regionEnd > regionStart && regionEnd < probeStart &&
               int.TryParse(source.AsSpan(regionStart, regionEnd - regionStart), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var region) &&
               region > 0 &&
               source.AsSpan(prefixStart, probeStart - prefixStart).SequenceEqual(
                   SpellingBeforeName(name.Ordinal, region)
               );
    }

    // What CppNameResolver writes in front of a function-scope name with this ordinal and region
    private static string SpellingBeforeName(int ordinal, int region) =>
        CppName.FunctionScope(ordinal, string.Empty, region).ToString();

    private static bool EndsWith(string source, int end, string text) =>
        end >= text.Length && source.AsSpan(end - text.Length, text.Length).SequenceEqual(text);
}
