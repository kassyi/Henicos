#nullable enable
using Kassyi.GSharp.Compiler.ErrorSites;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Checks the error sites the IR decides against the sites the syntax path generates for the same kernels, in both
/// directions: a site is reported by its ID, a hash of its place, so the IR must pick the same places (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrDecisionSiteOracleTests(ITestOutputHelper output)
{
    private const string TestCase = "Ir/IrDecisionSiteOracleCases.cu.cs";

    // The file name the site IDs hash; fixed so that the IDs do not depend on where the file is
    private const string FileName = "SiteOracleKernels.cu.cs";
    private static readonly string[] s_typeNames =
        ["SiteOracleKernels", "OracleExtensions", "StackLengthOracleKernels"];

    // [WHY] A device function reports its checks with the place as text, not through the site table, so the IDs are
    // compared for the kernel classes only (their device functions here have no check the table could hold)
    private static readonly string[] s_kernelTypeNames = ["SiteOracleKernels", "StackLengthOracleKernels"];

    // [WHY] Gpu.NotNull checks its own argument, a check the call rule writes and no decision of the IR holds
    private const string NotNullMessage = "G#: Gpu.NotNull was given a null array";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_EveryCheckSite_IsASiteTheSyntaxPathGenerates()
    {
        var compilation = Compile();
        var generated = GeneratedSites(compilation);
        Assert.NotEmpty(generated);

        var missing = new List<string>();
        var decided = new HashSet<uint>();
        var opaque = new List<Location>();
        var checkedSites = 0;
        foreach (var (symbol, built) in s_kernelTypeNames.SelectMany(name => BuiltMethods(compilation, name)))
        {
            // [WHY] Both trees count for the other direction: the split checks a receiver it moves where the receiver
            // is, as the method before the split decides it, and the access after the split checks the temporary
            var pre = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
            checkedSites += CheckSites(pre.Body, $"{symbol.Name} before the split", compilation, generated, missing,
                decided);
            if (Split(compilation, built, symbol) is { } machine)
            {
                var post = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
                checkedSites += CheckSites(post, $"{symbol.Name} after the split", compilation, generated, missing,
                    decided);
            }

            IrNodeChildren.DescendantsAndSelf(pre.Body)
                .Where(static node => node is IrOpaqueExpression or IrOpaqueStatement)
                .Select(node => node.Source.ToLocation(compilation))
                .ToList()
                .ForEach(opaque.Add);
        }

        var leftToSyntax = 0;
        foreach (var (id, (place, message)) in generated)
        {
            if (decided.Contains(id) || message == NotNullMessage)
            {
                continue;
            }

            // The IR decides nothing inside a statement or expression it leaves to the syntax path
            if (opaque.Any(location => location.SourceSpan.Contains(place)))
            {
                leftToSyntax++;
                continue;
            }

            missing.Add($"generated site {id} ({message}) at {place} has no decided site");
        }

        output.WriteLine($"Sites checked: {checkedSites}, generated: {generated.Count}, " +
                         $"left to the syntax path: {leftToSyntax}");
        Assert.True(checkedSites >= 20, $"Only {checkedSites} sites were checked.");
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_OracleKernels_AgreeWithTheSyntaxRulesBeforeAndAfterTheSplit()
    {
        var compilation = Compile();
        var counts = new IrNullAgreementCounts();
        var splitNodes = 0;
        foreach (var typeName in s_typeNames)
        {
            foreach (var (symbol, built) in BuiltMethods(compilation, typeName))
            {
                IrNullAgreementRunner.ComparePreSplit(compilation, symbol, built, counts);
                splitNodes += Math.Max(0, IrNullAgreementRunner.ComparePostSplit(compilation, symbol, built, counts));
            }
        }

        output.WriteLine($"call {counts.CallCount}, argCheck {counts.ArgumentCheckCount}, " +
                         $"member {counts.MemberCount}, managed {counts.ManagedCount}, split nodes {splitNodes}");
        Assert.True(counts.CallCount > 0 && counts.ArgumentCheckCount > 0 && splitNodes > 0);
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal)));
    }

    private static Compilation Compile()
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(FileName, RoslynTestUtility.ReadTestCase(TestCase));
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    // The ID, the place (as a position in the file) and the message of each GSharpErrorSite the generator writes for
    // this file in the CPU dialect
    private static Dictionary<uint, (int Place, string Message)> GeneratedSites(Compilation compilation)
    {
        var (_, generated, _) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");
        var lines = compilation.SyntaxTrees.Single(static t => Path.GetFileName(t.FilePath) == FileName).GetText()
            .Lines;
        var sites = new Dictionary<uint, (int Place, string Message)>();
        foreach (var tree in generated.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)))
        {
            foreach (var creation in tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (creation.Type is QualifiedNameSyntax { Right.Identifier.ValueText: "GSharpErrorSite" } &&
                    creation.ArgumentList?.Arguments is
                    [
                        { Expression: LiteralExpressionSyntax { Token.Value: uint id } },
                        { Expression: LiteralExpressionSyntax { Token.Value: FileName } },
                        { Expression: LiteralExpressionSyntax { Token.Value: int line } },
                        { Expression: LiteralExpressionSyntax { Token.Value: int column } }, _,
                        { Expression: LiteralExpressionSyntax { Token.Value: string message } }
                    ])
                {
                    sites[id] = (lines[line - 1].Start + column - 1, message);
                }
            }
        }

        return sites;
    }

    private static int CheckSites(IrNode root, string where, Compilation compilation,
        Dictionary<uint, (int Place, string Message)> generated, List<string> missing, HashSet<uint> decided)
    {
        var count = 0;
        foreach (var site in DecidedSites(root))
        {
            count++;
            var location = site.Source.ToLocation(compilation);
            var id = ErrorSiteTable.CreateSite(location, FileName, site.Message, site.TrapKind).SiteId;
            decided.Add(id);
            if (!generated.ContainsKey(id))
            {
                missing.Add($"{where}: {site.TrapKind} at {location.GetLineSpan().StartLinePosition} " +
                            $"[{location.SourceTree?.GetText().ToString(location.SourceSpan)}] id {id}");
            }
        }

        return count;
    }

    private static IEnumerable<IrErrorSite> DecidedSites(IrNode root)
    {
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            switch (node)
            {
                case IrElementReference { Decision: not null } element when element.Decided.Site is { } site:
                    yield return site;
                    break;
                case IrAssignment { Decision: not null } assignment
                    when assignment.Decided.Store?.Site is { } site:
                    yield return site;
                    break;
                case IrPropertyReference { Decision: not null } member when member.Decided.ReceiverSite is { } site:
                    yield return site;
                    break;
                case IrBinary { Decision: not null } binary when binary.Decided.Site is { } site:
                    yield return site;
                    break;
                case IrCompoundAssignment { Decision: not null } compound when compound.Decided.Site is { } site:
                    yield return site;
                    break;
                case IrCall { Decision: not null } call:
                    foreach (var argument in call.Decided.Arguments)
                    {
                        if (argument.Check.ArraySite is { } arraySite)
                        {
                            yield return arraySite;
                        }

                        foreach (var field in argument.Check.StructChecks)
                        {
                            yield return field.Site;
                        }
                    }

                    break;
            }
        }
    }

    private static IrStateMachine? Split(Compilation compilation, IrMethod built, IMethodSymbol symbol)
    {
        if (IrSplitLowering.Apply(IrClassifier.Classify(built, compilation), compilation, out _) is not { } lowered)
        {
            return null;
        }

        var linear = IrLinearizer.Linearize(lowered, compilation);
        return IrStateMachineBuilder.Build(linear, !symbol.HasAttribute(KnownAttributes.GpuKernel), built.Body.Source,
            compilation);
    }

    private static IEnumerable<(IMethodSymbol Symbol, IrMethod Built)> BuiltMethods(Compilation compilation,
        string typeName)
    {
        var type = compilation.GetTypeByMetadataName(typeName) ??
                   throw new InvalidOperationException($"Type '{typeName}' not found.");
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind == MethodKind.Ordinary &&
                IrBuilder.TryBuild(method, compilation, CancellationToken.None) is { } built)
            {
                yield return (method, built);
            }
        }
    }
}
