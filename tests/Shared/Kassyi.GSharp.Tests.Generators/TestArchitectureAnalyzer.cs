#nullable enable
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Enforces this repository's own test conventions. Repository-internal: it is not part of the shipped
/// <c>Kassyi.GSharp.Compiler</c> analyzer assembly, so it never runs in user projects (issue #460).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestArchitectureAnalyzer : DiagnosticAnalyzer
{
#pragma warning disable RS2008 // Repository-internal analyzer; not shipped, so analyzer release tracking does not apply.
    // Title and severity are carried over from the former diagnostics.json entry (formerly in the GSH range); the
    // message names the attributes that count. IDs use the repository-internal HNT prefix.
    public static readonly DiagnosticDescriptor TestMethodMustHaveCategoryTrait = new(
        id: "HNT0002",
        title: "Test methods must have a Category trait",
        messageFormat: "Test method '{0}' has no category. Add [Category(TestCategory.X)], [TestTarget(ExecutionTarget.X)] or [Scenario(TestScenario.X)] to the method or its class.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RawTraitAttributeNotAllowed = new(
        id: "HNT0008",
        title: "Raw [Trait] attribute in a test assembly",
        messageFormat: "[Trait] on '{0}' is not allowed. Use [Category(TestCategory.X)], [TestTarget(ExecutionTarget.X)] or [Scenario(TestScenario.X)] instead.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TestMethodMustHaveCategoryTrait, RawTraitAttributeNotAllowed);

    public override void Initialize(AnalysisContext context)
    {
        // tests/** is generated code in .editorconfig, so the analyzer has to opt in to it
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            if (!TestSymbols.IsTestAssembly(compilation) ||
                compilation.GetTypeByMetadataName(TestSymbols.FactAttributeMetadataName) is not { } factAttribute)
            {
                return;
            }

            var traitAttribute = compilation.GetTypeByMetadataName("Xunit.TraitAttribute");

            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeClass(context, traitAttribute),
                SyntaxKind.ClassDeclaration
            );
            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeMethod(context, factAttribute, traitAttribute),
                SyntaxKind.MethodDeclaration
            );
        });
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context, INamedTypeSymbol? traitAttribute)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
        {
            return;
        }

        ReportRawTraits(context, classSymbol, traitAttribute);
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol factAttribute,
        INamedTypeSymbol? traitAttribute)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol == null)
        {
            return;
        }

        ReportRawTraits(context, methodSymbol, traitAttribute);

        if (!TestSymbols.IsTestMethod(methodSymbol, factAttribute))
        {
            return;
        }

        if (methodDeclaration.Parent is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
        {
            return;
        }

        // Check for Category trait on method or class
        var hasCategoryTrait = HasCategoryTrait(methodSymbol) || HasCategoryTrait(classSymbol);
        if (!hasCategoryTrait)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TestMethodMustHaveCategoryTrait,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name));
        }
    }

    private static void ReportRawTraits(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        INamedTypeSymbol? traitAttribute)
    {
        if (traitAttribute == null)
        {
            return;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, traitAttribute))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? (symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None);
            context.ReportDiagnostic(Diagnostic.Create(
                RawTraitAttributeNotAllowed,
                location,
                symbol.Name));
        }
    }

    private static bool IsAttribute(AttributeData attribute, string shortName)
    {
        var name = attribute.AttributeClass?.Name;
        return name == shortName || name == shortName + "Attribute";
    }

    private static bool HasCategoryTrait(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsAttribute(attribute, "Category") ||
                IsAttribute(attribute, "TestCategory") ||
                IsAttribute(attribute, "TestTarget") ||
                IsAttribute(attribute, "Scenario"))
            {
                return true;
            }
        }

        return false;
    }
}
