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
    private const string TestPrefix = "Test";
    // <Unit>_<Condition> has one separator, <Unit>_<Condition>_<Expected>_<Detail> three.
    private const int MinSeparators = 1;
    private const int MaxSeparators = 3;

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

        var rest = name;
        var separators = 0;
        while (true)
        {
            var index = rest.IndexOf('_');
            var part = index < 0 ? rest : rest.Slice(0, index);
            if (!IsValidPart(part, isFirstPart: separators == 0))
            {
                return false;
            }

            if (index < 0)
            {
                return separators >= MinSeparators;
            }

            separators++;
            if (separators > MaxSeparators)
            {
                return false;
            }

            rest = rest.Slice(index + 1);
        }
    }

    // The first part starts with an upper-case letter; the others may also start with a digit (Grid1D, 64Kb).
    private static bool IsValidPart(ReadOnlySpan<char> part, bool isFirstPart)
    {
        if (part.IsEmpty)
        {
            return false;
        }

        var startsCorrectly = IsAsciiUpper(part[0]) || (!isFirstPart && IsAsciiDigit(part[0]));
        if (!startsCorrectly)
        {
            return false;
        }

        for (var i = 1; i < part.Length; i++)
        {
            if (!IsAsciiLetterOrDigit(part[i]))
            {
                return false;
            }
        }

        return true;
    }

    // "Test", "Test_..." and "Test<Upper>..." are prefixes; "Testing_..." and "Tester_..." are ordinary units.
    private static bool HasForbiddenTestPrefix(ReadOnlySpan<char> name) =>
        name.StartsWith("Test".AsSpan(), StringComparison.Ordinal) &&
        (name.Length == TestPrefix.Length || name[TestPrefix.Length] == '_' || IsAsciiUpper(name[TestPrefix.Length]));

    private static bool IsAsciiUpper(char c) => c is >= 'A' and <= 'Z';

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetterOrDigit(char c) => IsAsciiUpper(c) || c is >= 'a' and <= 'z' || IsAsciiDigit(c);
}
