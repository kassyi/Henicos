
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
        messageFormat: "Test method '{0}' has no category. Add [Category(TestCategory.X)], [TestTarget(ExecutionTarget.X)], [Scenario(TestScenario.X)] or [Trait(\"Category\", \"...\")] to the method or its class.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TestMethodMustHaveCategoryTrait);

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

            compilationContext.RegisterSyntaxNodeAction(
                context => AnalyzeMethod(context, factAttribute),
                SyntaxKind.MethodDeclaration
            );
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, INamedTypeSymbol factAttribute)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol == null || !TestSymbols.IsTestMethod(methodSymbol, factAttribute))
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

            if (!IsAttribute(attribute, "Trait"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var firstArg = attribute.ConstructorArguments[0];
            if (firstArg.Value is "Category")
            {
                return true;
            }
        }

        return false;
    }
}
