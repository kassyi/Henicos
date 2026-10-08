using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TestArchitectureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        DiagnosticRuleRegistry.AllSupportedDiagnostics.Add(GSharpDiagnostics.TestClassMustInheritFromBase)
            .Add(GSharpDiagnostics.TestMethodMustHaveCategoryTrait);

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
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        // Check if method is a test method
        var isTestMethod = false;
        foreach (var attribute in methodSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is not ("FactAttribute" or "TheoryAttribute"))
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

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
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
            context.Report(
                GSharpDiagnostics.TestClassMustInheritFromBase,
                classDeclaration.Identifier.GetLocation(),
                classDeclaration.Identifier.Text);
        }

        // Check for Category trait on method or class
        var hasCategoryTrait = HasCategoryTrait(methodSymbol) || HasCategoryTrait(classSymbol);
        if (!hasCategoryTrait)
        {
            context.Report(
                GSharpDiagnostics.TestMethodMustHaveCategoryTrait,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
        }
    }

    private static bool HasCategoryTrait(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attrName = attribute.AttributeClass?.Name;
            if (attrName is "CategoryAttribute" or "TestCategoryAttribute" or "TestTargetAttribute" or "ScenarioAttribute")
            {
                return true;
            }

            if (attrName != "TraitAttribute")
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
