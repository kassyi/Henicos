#nullable enable
using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Enforces test method naming conventions (HNT0009). Repository-internal: it is not part of the shipped
/// <c>Kassyi.GSharp.Compiler</c> analyzer assembly, so it never runs in user projects.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestMethodNamingAnalyzer : DiagnosticAnalyzer
{
    // Repository-internal analyzer; not shipped, so analyzer release tracking does not apply.
#pragma warning disable RS2008
    public static readonly DiagnosticDescriptor TestMethodNamingConvention = new(
        id: "HNT0009",
        title: "Test method name must follow the <Unit>_<Condition>[_<Expected>] convention without 'Test' prefix",
        messageFormat: "Test method '{0}' does not follow the test naming convention. Remove the 'Test' or 'Test_' " +
            "prefix (every test carries [Category], [TestTarget] and [Scenario] per HNT0002) and format the name as " +
            "'<Unit>_<Condition>[_<Expected>]'.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Test methods must be named '<Unit>_<Condition>[_<Expected>]' in PascalCase parts separated by " +
            "underscores. The 'Test' or 'Test_' prefix is forbidden because [Fact]/[Theory] and the [Category], " +
            "[TestTarget] and [Scenario] attributes (HNT0002) already declare that it is a test and classify its " +
            "scenario, and the prefix obscures the unit under test in test lists.");
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TestMethodNamingConvention);

    public override void Initialize(AnalysisContext context)
    {
        // tests/** is generated code in .editorconfig, so the analyzer has to opt in to it
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            if (!TestSymbols.IsTestAssembly(compilation) ||
                compilation.GetTypeByMetadataName(TestSymbols.FactAttributeMetadataName) is not { } factAttribute)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeMethod(context, factAttribute),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, INamedTypeSymbol factAttribute)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol == null)
        {
            return;
        }

        if (!TestSymbols.IsTestMethod(methodSymbol, factAttribute))
        {
            return;
        }

        var methodName = methodDeclaration.Identifier.ValueText;
        if (!IsValidTestMethodName(methodName.AsSpan()))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TestMethodNamingConvention,
                methodDeclaration.Identifier.GetLocation(),
                methodName));
        }
    }

    /// <summary>
    /// Validates whether a test method name follows the <c>&lt;Unit&gt;_&lt;Condition&gt;[_&lt;Expected&gt;]</c>
    /// convention without a forbidden <c>Test</c> or <c>Test_</c> prefix.
    /// </summary>
    public static bool IsValidTestMethodName(string? name) =>
        !string.IsNullOrEmpty(name) && IsValidTestMethodName(name.AsSpan());

    /// <summary>
    /// Validates whether a test method name span follows the convention without allocation.
    /// Equivalent to regex: <c>^[A-Z][A-Za-z0-9]*(_[A-Z0-9][A-Za-z0-9]*){1,3}$</c> excluding <c>Test</c> prefixes.
    /// </summary>
    public static bool IsValidTestMethodName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || HasForbiddenTestPrefix(name))
        {
            return false;
        }

        var underscoreCount = 0;
        var partStartIndex = 0;

        for (var i = 0; i <= name.Length; i++)
        {
            if (i != name.Length && name[i] != '_')
            {
                continue;
            }

            var partLength = i - partStartIndex;
            if (partLength == 0)
            {
                return false;
            }

            var part = name.Slice(partStartIndex, partLength);
            if (!IsValidPart(part, isFirstPart: underscoreCount == 0))
            {
                return false;
            }

            if (i < name.Length)
            {
                underscoreCount++;
                if (underscoreCount > 3)
                {
                    return false;
                }

                partStartIndex = i + 1;
            }
        }

        return underscoreCount is >= 1 and <= 3;
    }

    private static bool IsValidPart(ReadOnlySpan<char> part, bool isFirstPart)
    {
        if (part.IsEmpty)
        {
            return false;
        }

        var firstChar = part[0];
        if (isFirstPart)
        {
            if (firstChar is < 'A' or > 'Z')
            {
                return false;
            }
        }
        else
        {
            if (firstChar is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        for (var j = 1; j < part.Length; j++)
        {
            var c = part[j];
            if (c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasForbiddenTestPrefix(ReadOnlySpan<char> name)
    {
        if (name.Length >= 5 && name[0] == 'T' && name[1] == 'e' && name[2] == 's' && name[3] == 't')
        {
            if (name[4] == '_' || (name[4] >= 'A' && name[4] <= 'Z'))
            {
                return true;
            }
        }
        else if (name.Length == 4 && name[0] == 'T' && name[1] == 'e' && name[2] == 's' && name[3] == 't')
        {
            return true;
        }

        return false;
    }
}
