
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
    // IDs, titles, messages and severities are carried over unchanged from the former diagnostics.json entries.
    public static readonly DiagnosticDescriptor TestClassMustInheritFromBase = new(
        id: "GSH0049",
        title: "Test classes must inherit from required base classes",
        messageFormat: "Test class '{0}' contains [Fact] or [Theory] methods but does not inherit from TranspilerTestBase or StateMachineTestBase. This is required by the G# test architecture guidelines.",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TestMethodMustHaveCategoryTrait = new(
        id: "GSH0050",
        title: "Test methods must have a Category trait",
        messageFormat: "Test method '{0}' is missing [Trait(\"Category\", \"...\")]. All tests must be categorized (e.g., Compiler, SadPath, GpuExecution).",
        category: "GSharpTestArchitecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(TestClassMustInheritFromBase, TestMethodMustHaveCategoryTrait);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            // Only run in test assemblies
            if (compilationContext.Compilation.AssemblyName?.Contains("Test") != true)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol == null)
        {
            return;
        }

        // Check if method is a test method
        var isTestMethod = false;
        foreach (var attribute in methodSymbol.GetAttributes())
        {
            if (!IsAttribute(attribute, "Fact") && !IsAttribute(attribute, "Theory"))
            {
                continue;
            }

            isTestMethod = true;
            break;
        }

        if (!isTestMethod)
        {
            return;
        }

        // Check if class inherits from required base
        if (methodDeclaration.Parent is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol == null)
        {
            return;
        }

        var inheritsRequiredBase = false;
        var currentBase = classSymbol.BaseType;
        while (currentBase != null)
        {
            if (currentBase.Name is "TranspilerTestBase" or "StateMachineTestBase" or "DocTestBase")
            {
                inheritsRequiredBase = true;
                break;
            }

            currentBase = currentBase.BaseType;
        }

        if (!inheritsRequiredBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TestClassMustInheritFromBase,
                classDeclaration.Identifier.GetLocation(),
                classDeclaration.Identifier.Text));
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
