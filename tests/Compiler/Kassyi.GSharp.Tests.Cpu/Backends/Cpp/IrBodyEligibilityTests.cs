#nullable enable
using Kassyi.GSharp.Compiler.Backends;
using Kassyi.GSharp.Compiler.Backends.Cpp;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Bodies;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Backends.Cpp;

/// <summary>
/// Which device functions of the IR corpus take the IR route of <see cref="IrBodyRoute.NativePlain"/>, and why the
/// others do not (<c>IrEligibility.NativePlain.expected.yml</c>). The population is what the emission plan writes as a
/// body without a state machine: the device functions of <see cref="MethodEmissionKind.DeviceFunction"/> with a symbol
/// and a body that are not kernel entries. A pull request that teaches the C++ writer a form lowers the count of
/// <see cref="IrBodyIneligibility.UnsupportedForm"/>.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrBodyEligibilityTests(ITestOutputHelper output)
{
    private static readonly string[] s_header =
    [
        "Which device functions of the IR corpus (the files of TestCases/ and every kernel file) take the IR route of",
        "NativePlain. IrBodyEligibilityTests.Eligibility_Corpus_MatchesTheExpectedFile builds the tables again",
        "on every run and fails on any difference; a received file goes to tmp/.",
        "",
        "  kinds:        the emission kind of every GPU method (the population is DeviceFunction with a body)",
        "  reasons:      why a method of the population takes no IR route (IrBodyIneligibility); None is eligible",
        "  opaque:       the opaque node that stops a method (reason/syntax kind), most first",
        "  split:        the kind of the split call that stops a method",
        "  firstHit:     the first node the C++ writer cannot spell, per method, most first",
        "  occurrences:  every node the C++ writer cannot spell, most first",
        "  eligible:     the methods of the population with an IR body"
    ];

    private static readonly Lazy<IReadOnlyList<Entry>> s_entries =
        new(() => Scan(CppBodyProfile.For(new StandardCppDialect())));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Eligibility_Corpus_MatchesTheExpectedFile()
    {
        var entries = s_entries.Value;
        var rendered = Render(entries);
        output.WriteLine(rendered);

        IrExpectedFiles.AssertMatches(ExpectedFileFor(IrBodyRoute.NativePlain), rendered, output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Eligibility_EligibleMethods_HaveNoSplitCall()
    {
        var eligible = s_entries.Value
            .Where(static entry => entry is { InPopulation: true, Reason: IrBodyIneligibility.None })
            .ToArray();

        Assert.NotEmpty(eligible);
        Assert.All(eligible, static entry => Assert.False(entry.HoldsSplitCall, entry.Method.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Eligibility_SplitCallMethods_AreTheRecursiveSuspendingFunctions()
    {
        var population = s_entries.Value.Where(static entry => entry.InPopulation).ToArray();
        var split = population
            .Where(static entry => entry.Reason == IrBodyIneligibility.SplitCall)
            .Select(static entry => entry.Method)
            .OrderBy(static method => method.ToString(), StringComparer.Ordinal)
            .ToArray();
        var recursive = population
            .Where(static entry => entry.IsRecursiveSuspending)
            .Select(static entry => entry.Method)
            .OrderBy(static method => method.ToString(), StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(recursive);
        Assert.Equal(recursive, split);

        var removedBarrier = Assert.Single(population,
            static entry => entry.Method.Method == "RemovedCallOps.CallsOnlyRemovedBarrier");
        var removedShuffle = Assert.Single(population,
            static entry => entry.Method.Method == "RemovedCallOps.ShufflesOnlyInRemovedCall");
        Assert.Equal(IrBodyIneligibility.RemovedCall, removedBarrier.Reason);
        Assert.Equal(IrBodyIneligibility.RemovedCall, removedShuffle.Reason);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Eligibility_Corpus_ReachesTheWriterRows()
    {
        var reasons = s_entries.Value
            .Where(static entry => entry.InPopulation)
            .Select(static entry => entry.Reason)
            .ToArray();

        Assert.Contains(IrBodyIneligibility.None, reasons);
        Assert.Contains(IrBodyIneligibility.OpaqueNode, reasons);
        Assert.Contains(IrBodyIneligibility.SplitCall, reasons);
        Assert.Contains(IrBodyIneligibility.UnsupportedForm, reasons);
    }

    internal static string ExpectedFileFor(IrBodyRoute route) => $"IrEligibility.{route}.expected.yml";

    internal static IReadOnlyList<Entry> Scan(CppBodyProfile profile)
    {
        var entries = new ConcurrentBag<Entry>();
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is { } symbol && GpuExecutionSpace.IsGpuFunction(symbol))
                    {
                        entries.Add(Classify(declaration, model, symbol, compilation, tree, profile));
                    }
                }
            }
        }, everyKernelFile: true);
        return [.. entries];
    }

    // The same population as the emission plan's choice of the C++ writer: a device function body without a state
    // machine, with a symbol and a body, not a kernel entry; a generic definition that the generator leaves out never
    // reaches the plan
    private static Entry Classify(MethodDeclarationSyntax declaration, SemanticModel model, IMethodSymbol symbol,
        Compilation compilation, SyntaxTree tree, CppBodyProfile profile)
    {
        var method = IrCoverageInputs.Locate(tree, symbol);
        // The plan of a kernel is always of the kernel kind (MethodEmissionPlan.Create), and making it names the
        // kernel, which a sad-path file of the corpus cannot do
        if (symbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return new Entry(method, MethodEmissionKind.Kernel, false, IrBodyIneligibility.None, "", [], false, false);
        }

        var plan = MethodEmissionPlan.Create(declaration, model, isKernel: false);
        if (plan.Kind != MethodEmissionKind.DeviceFunction || plan.Symbol is null || !declaration.HasBody() ||
            plan.IsGpuKernelEntry || GenericDefinitions.IsLeftOut(symbol, compilation))
        {
            return new Entry(method, plan.Kind, false, IrBodyIneligibility.None, "", [], false, false);
        }

        var front = IrBodyFronts.Create(symbol, compilation, CancellationToken.None);
        var result = IrBodyLowering.TryLower(front, profile, CancellationToken.None);
        var holdsSplitCall = result.Body is { Front.Classified: { } classified } &&
            IrNodeChildren.Exists(classified.Body,
                static node => node is IrCall { Classification.Split.IsSplit: true });
        return new Entry(method, plan.Kind, true, result.Reason, result.Detail,
            [.. result.Unsupported.Select(static key => key.ToString())], holdsSplitCall,
            SuspensionAnalysis.IsRecursiveSuspendingFunction(symbol, compilation));
    }

    internal static string Render(IReadOnlyList<Entry> entries)
    {
        var population = entries.Where(static entry => entry.InPopulation).ToArray();
        var lines = new List<string>();
        lines.AddRange(s_header.Select(static line => line.Length == 0 ? "#" : "# " + line));
        lines.Add("kinds:");
        lines.AddRange(Enum.GetValues<MethodEmissionKind>()
            .Select(kind => $"  {kind}: {entries.Count(entry => entry.Kind == kind)}"));
        lines.Add("reasons:");
        lines.AddRange(Enum.GetValues<IrBodyIneligibility>()
            .Select(reason => $"  {reason}: {population.Count(result => result.Reason == reason)}"));
        AddTable(lines, "opaque", population
            .Where(static result => result.Reason == IrBodyIneligibility.OpaqueNode)
            .Select(static result => result.Detail));
        AddTable(lines, "split", population
            .Where(static result => result.Reason == IrBodyIneligibility.SplitCall)
            .Select(static result => result.Detail));
        AddTable(lines, "firstHit", population
            .Where(static result => result.Reason == IrBodyIneligibility.UnsupportedForm)
            .Select(static result => result.Unsupported[0]));
        AddTable(lines, "occurrences", population
            .SelectMany(static result => result.Unsupported));
        lines.Add($"eligible: {population.Count(static result => result.Reason == IrBodyIneligibility.None)}");
        return string.Join("\n", lines) + "\n";
    }

    // The counts of each value, most first, then by ordinal
    private static void AddTable(List<string> lines, string name, IEnumerable<string> values)
    {
        lines.Add(name + ":");
        lines.AddRange(values
            .GroupBy(static value => value, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => $"  - {{key: \"{group.Key}\", count: {group.Count()}}}"));
    }

    // A GPU method of the corpus: its emission kind and, in the population, the result of the IR route (kept as
    // values, so that the trees and their compilations are not held for the other tests of the class)
    internal sealed record Entry(IrCorpusMethod Method, MethodEmissionKind Kind, bool InPopulation,
        IrBodyIneligibility Reason, string Detail, string[] Unsupported, bool HoldsSplitCall,
        bool IsRecursiveSuspending);
}
