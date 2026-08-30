using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_02_ObjectLifecycleSadPathTests
{
    private static async Task<(SyntaxTree SyntaxTree, SemanticModel SemanticModel)> GetParsedModelAsync()
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        var path = Path.Combine(testCasesDir, "Analyzers", "MEM-02_ObjectLifecycleSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = Helpers.MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        return (syntaxTree, semanticModel);
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_MissingIDisposableOnClass_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassMissingIDisposable");

        var classSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        Assert.NotNull(classSymbol);

        // Verify class has disposable field but does not implement IDisposable
        var hasDisposableField = classSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => f.Type.AllInterfaces.Any(i => i.Name == "IDisposable"));

        var implementsIDisposable = classSymbol.AllInterfaces
            .Any(i => i.Name == "IDisposable");

        Assert.True(hasDisposableField, "Class should have a disposable field");
        Assert.False(implementsIDisposable, "Class should not implement IDisposable");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_UndisposedFieldInDispose_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassUndisposedField");

        var disposeMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Dispose");

        // Verify AST does not contain invocation of _c.Dispose()
        var invocations = disposeMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();

        var disposesField = invocations.Any(inv => inv.ToString().Contains("_c.Dispose"));

        Assert.False(disposesField, "Dispose() AST should not contain _c.Dispose() invocation");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public async Task Test_MEM02_11_3_1_CleanChainedDisposal_Validated()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassCleanChainedDisposal");

        var classSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        Assert.NotNull(classSymbol);
        Assert.Contains(classSymbol.AllInterfaces, i => i.Name == "IDisposable");

        var disposeMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Dispose");

        var hasFieldDispose = disposeMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("_c.Dispose"));

        Assert.True(hasFieldDispose, "Dispose() should contain _c.Dispose() invocation for clean chained disposal");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_UnsafeFieldOverwrite_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassUnsafeFieldOverwrite");

        var updateMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Update");

        var assignments = updateMethod.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .ToList();

        Assert.NotEmpty(assignments);
        var fieldAssignment = assignments.First(a => a.Left.ToString() == "_c");

        // Verify assignment creates new instance without prior disposal statement in method
        var isNewCreation = fieldAssignment.Right is ObjectCreationExpressionSyntax;
        var hasPriorDispose = updateMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("_c.Dispose"));

        Assert.True(isNewCreation, "Field should be reassigned with new instance");
        Assert.False(hasPriorDispose, "Field is reassigned without prior disposal");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_2_PropertyGetterSideEffects_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassPropertyGetterSideEffect");

        var propDecl = classDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .First(p => p.Identifier.Text == "Context");

        var allocatesInGetter = propDecl.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Any();

        Assert.True(allocatesInGetter, "Property getter side-effect allocating disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_2_ImplicitCastOperators_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var wrapperClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "MyWrapper");

        var castOp = wrapperClass.Members
            .OfType<ConversionOperatorDeclarationSyntax>()
            .First(c => c.ImplicitOrExplicitKeyword.Text == "implicit");

        var allocatesInCast = castOp.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Any();

        Assert.True(allocatesInCast, "Implicit cast operator allocating disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_3_UnguardedMultiAllocation_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassUnguardedMultiAllocation");

        var ctor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .First();

        var allocations = ctor.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .ToList();

        var hasTryCatch = ctor.DescendantNodes()
            .OfType<TryStatementSyntax>()
            .Any();

        Assert.True(allocations.Count >= 2, "Constructor contains multiple allocations");
        Assert.False(hasTryCatch, "Multiple allocations are unguarded without try-catch");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public async Task Test_MEM02_11_3_3_FactoryPattern_Validated()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "ClassFactoryPattern");

        var createMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Create");

        var tryStatement = createMethod.DescendantNodes()
            .OfType<TryStatementSyntax>()
            .FirstOrDefault();

        Assert.NotNull(tryStatement);
        var catchHasDispose = tryStatement.Catches
            .SelectMany(c => c.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Any(inv => inv.ToString().Contains("Dispose"));

        Assert.True(catchHasDispose, "Factory pattern should dispose created resource on catch");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_3_StaticLifecycle_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "GpuCache");

        var staticField = classDecl.Members
            .OfType<FieldDeclarationSyntax>()
            .First(f => f.Modifiers.Any(m => m.Text == "static"));

        var isDisposableAllocation = staticField.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Any();

        Assert.True(isDisposableAllocation, "Static field allocation of disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public async Task Test_MEM02_11_3_4_StandardInjectionBorrowing_HazardDetected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "BorrowedResourceClass");

        var ctor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .First();

        var param = ctor.ParameterList.Parameters.First();
        var paramSymbol = model.GetDeclaredSymbol(param);
        Assert.NotNull(paramSymbol);

        var hasTakesOwnership = paramSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "TakesOwnershipAttribute");

        var disposeMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Dispose");

        var disposesBorrowedField = disposeMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("_c.Dispose"));

        Assert.False(hasTakesOwnership, "Parameter does not have TakesOwnership attribute (borrowed)");
        Assert.True(disposesBorrowedField, "Disposing borrowed parameter in class Dispose is a double-dispose hazard");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public async Task Test_MEM02_11_3_4_ExplicitOwnershipTransfer_Validated()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "OwnedResourceClass");

        var ctor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .First();

        var param = ctor.ParameterList.Parameters.First();
        var paramSymbol = model.GetDeclaredSymbol(param);
        Assert.NotNull(paramSymbol);

        var hasTakesOwnership = paramSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "TakesOwnershipAttribute");

        Assert.True(hasTakesOwnership, "Parameter should have TakesOwnership attribute");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_5_DefaultInterfaceMethods_Detected()
    {
        var (tree, model) = await GetParsedModelAsync();
        var root = await tree.GetRootAsync();

        var interfaceDecl = root.DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>()
            .First(i => i.Identifier.Text == "IResourceBlackbox");

        var dimMethod = interfaceDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Process");

        var hasBody = dimMethod.Body != null || dimMethod.ExpressionBody != null;
        var disposesInDIM = dimMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("Dispose"));

        Assert.True(hasBody, "Interface method should be a Default Interface Method (DIM) with body");
        Assert.True(disposesInDIM, "DIM taking ownership/disposing parameter must be detected");
    }
}
