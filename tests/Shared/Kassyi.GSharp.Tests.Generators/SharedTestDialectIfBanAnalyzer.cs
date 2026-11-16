#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Enforces that Tests.Shared does not contain GSHARP_CPU or GSHARP_CUDA preprocessor directives (HNT0010).
/// Dialect-specific tests must be placed in Tests.Cpu or Tests.Cuda, or separated into partial classes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SharedTestDialectIfBanAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HNT0010";
    private const string TargetDirectoryName = "Kassyi.GSharp.Tests.Shared";
    private const string SymbolGsharpCpu = "GSHARP_CPU";
    private const string SymbolGsharpCuda = "GSHARP_CUDA";

#pragma warning disable RS2008 // Repository-internal analyzer; not shipped, so analyzer release tracking does not apply.
    public static readonly DiagnosticDescriptor DialectIfInSharedTestNotAllowed = new(
        id: DiagnosticId,
        title: "Dialect preprocessor symbol not allowed in Tests.Shared",
        messageFormat: "Dialect preprocessor symbol '{0}' is not allowed in Tests.Shared. Move dialect-specific tests to Tests.Cpu or Tests.Cuda.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Tests.Shared must only contain tests and helpers shared across both Tests.Cpu and Tests.Cuda assemblies. " +
                     "Dialect-specific code should be placed in Tests.Cpu or Tests.Cuda, or separated into partial classes per test-architecture.md.");
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DialectIfInSharedTestNotAllowed);

    public override void Initialize(AnalysisContext context)
    {
        // tests/** is generated code in .editorconfig, so the analyzer has to opt in to it
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        var tree = context.Tree;
        if (!IsSharedTestTree(tree.FilePath))
        {
            return;
        }

        var root = tree.GetCompilationUnitRoot(context.CancellationToken);
        if (!root.ContainsDirectives)
        {
            return;
        }

        foreach (var conditional in root.DescendantNodes(descendIntoTrivia: true).OfType<ConditionalDirectiveTriviaSyntax>())
        {
            foreach (var identifier in conditional.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbolName = identifier.Identifier.ValueText;
                if (symbolName is SymbolGsharpCpu or SymbolGsharpCuda)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DialectIfInSharedTestNotAllowed,
                        identifier.GetLocation(),
                        symbolName));
                }
            }
        }
    }

    /// <summary>
    /// Checks whether the syntax tree belongs to Tests.Shared.
    /// </summary>
    public static bool IsSharedTestTree(string? filePath)
    {
        if (filePath is null || filePath.Length == 0)
        {
            return false;
        }

        return filePath.IndexOf(TargetDirectoryName, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
