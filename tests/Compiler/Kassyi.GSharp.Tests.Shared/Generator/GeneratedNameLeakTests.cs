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
/// A name written past the resolver leaves no record, so the test finds it by renaming: every user name declared in a GPU
/// method is renamed at the symbol level to a unique probe name (<see cref="UserNameProbeRenamer"/>) before the
/// transpilation. Every spelling the resolver produces for such a name (<c>AppendCppName</c>:
/// <c>__gsharp_id_{ordinal}_</c>, <c>__gsharp_il_{region}_{ordinal}_</c>, <c>__gsharp_mb_</c>) ends in <c>_</c> right
/// before the name, so a probe that follows anything else was written by another emission site.
/// </para>
/// <para>
/// A <c>nameof</c> keeps the original name as its value (the renamer replaces it with its string literal), so its text
/// never carries a probe. String literals and comments of the source are not renamed either.
/// </para>
/// <para>GCNR is the test-only family for internal consistency checks; it never appears in <c>diagnostics.json</c>.</para>
/// </remarks>
[Collection(TestCollections.CorpusScan)]
public class GeneratedNameLeakTests
{
    private const string LeakId = "GCNR0001";

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void GeneratedCode_TranspilerEmission_DoesNotLeakRawUserIdentifiers()
    {
        var renamer = UserNameProbeRenamer.Rename(RoslynTestUtility.SharedCompilation, IsGpuMethod);
        var leaks = new List<string>();
        var transpiled = 0;
        var resolved = 0;

        foreach (var (method, dialect, source) in TranspileGpuMethods(renamer.Compilation))
        {
            transpiled++;
            foreach (var (probe, throughResolver) in FindProbes(source))
            {
                if (throughResolver)
                {
                    resolved++;
                    continue;
                }

                leaks.Add($"{method.ContainingType.Name}.{method.Name} ({dialect.GetType().Name}): " +
                          renamer.GetOriginalName(probe));
            }
        }

        Assert.True(renamer.ProbeCount > 0, "No user name was renamed; the scan did not run.");
        Assert.True(transpiled > 0, "No GPU method was transpiled; the scan did not run.");
        Assert.True(resolved > 0, "No renamed user name reached the generated code through the resolver.");
        Assert.True(leaks.Count == 0,
            $"{LeakId}: raw user identifiers in generated code:" + Environment.NewLine +
            string.Join(Environment.NewLine, leaks.Distinct()));
    }

    // The C++ of every GPU method of the compilation that transpiles without a failure, on both dialects
    private static IEnumerable<(IMethodSymbol Method, ITranspilerDialect Dialect, string Source)> TranspileGpuMethods(
        Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method) is not { } symbol || !IsGpuMethod(symbol))
                {
                    continue;
                }

                foreach (var dialect in new ITranspilerDialect[] { new GSharpCudaDialect(), new StandardCppDialect() })
                {
                    var failures = new List<TranspileFailure>();
                    var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
                    var source = MethodTranspiler.GenerateCudaSource(method, model, failures.Add, isDevice, dialect,
                        out _);
                    if (failures.Count == 0)
                    {
                        yield return (symbol, dialect, source);
                    }
                }
            }
        }
    }

    private static bool IsGpuMethod(IMethodSymbol symbol) =>
        symbol.HasAttribute(KnownAttributes.GpuKernel) || symbol.HasAttribute(KnownAttributes.GpuDeviceFunction);

    // Every probe in the generated code: its number, and whether it ends a spelling of the resolver
    private static IEnumerable<(int Probe, bool ThroughResolver)> FindProbes(string source)
    {
        var start = source.IndexOf(UserNameProbeRenamer.Stem, StringComparison.Ordinal);
        while (start >= 0)
        {
            var digits = start + UserNameProbeRenamer.Stem.Length;
            var end = source.IndexOf(UserNameProbeRenamer.Terminator, digits);
            var probe = int.Parse(source.AsSpan(digits, end - digits), CultureInfo.InvariantCulture);
            yield return (probe, start > 0 && source[start - 1] == '_');
            start = source.IndexOf(UserNameProbeRenamer.Stem, end, StringComparison.Ordinal);
        }
    }
}
