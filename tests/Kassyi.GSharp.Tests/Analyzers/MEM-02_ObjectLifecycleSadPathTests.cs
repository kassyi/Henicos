using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_02_ObjectLifecycleSadPathTests
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Register()
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        var files = Directory.GetFiles(Path.Combine(testCasesDir, "Analyzers"), "MEM-02_*.cs");
        foreach (var f in files)
        {
            MegaCompilationCache.RegisterSource(File.ReadAllText(f));
        }
    }

    private static (SyntaxTree SyntaxTree, SemanticModel SemanticModel, INamedTypeSymbol TypeSymbol, TypeDeclarationSyntax TypeDecl) GetParsedModel(string typeName)
    {
        var typeSymbol = MegaCompilationCache.GetNamedTypeSymbol(typeName);
        var tree = typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        var model = MegaCompilationCache.BaseCompilation.GetSemanticModel(tree);
        var typeDecl = (TypeDeclarationSyntax)typeSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        return (tree, model, typeSymbol, typeDecl);
    }

    private static bool HasSyntaxInMember<TMember, TSyntax>(
        string typeName,
        System.Func<TMember, bool> memberPredicate,
        System.Func<TSyntax, bool> syntaxPredicate = null)
        where TMember : MemberDeclarationSyntax
        where TSyntax : SyntaxNode
    {
        var (_, _, _, typeDecl) = GetParsedModel(typeName);
        var member = typeDecl.Members.OfType<TMember>().First(memberPredicate);
        return syntaxPredicate == null 
            ? member.DescendantNodes().OfType<TSyntax>().Any()
            : member.DescendantNodes().OfType<TSyntax>().Any(syntaxPredicate);
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public void Test_MEM02_11_3_1_MissingIDisposableOnClass_Detected()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("ClassMissingIDisposable");
        var classDecl = (ClassDeclarationSyntax)typeDecl;
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
    public void Test_MEM02_11_3_1_UndisposedFieldInDispose_Detected()
    {
        var disposesField = HasSyntaxInMember<MethodDeclarationSyntax, InvocationExpressionSyntax>(
            "ClassUndisposedField",
            m => m.Identifier.Text == "Dispose",
            inv => inv.ToString().Contains("_c.Dispose"));

        Assert.False(disposesField, "Dispose() AST should not contain _c.Dispose() invocation");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_11_3_1_CleanChainedDisposal_Validated()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("ClassCleanChainedDisposal");
        Assert.NotNull(classSymbol);
        Assert.Contains(classSymbol.AllInterfaces, i => i.Name == "IDisposable");

        var hasFieldDispose = HasSyntaxInMember<MethodDeclarationSyntax, InvocationExpressionSyntax>(
            "ClassCleanChainedDisposal",
            m => m.Identifier.Text == "Dispose",
            inv => inv.ToString().Contains("_c.Dispose"));

        Assert.True(hasFieldDispose, "Dispose() should contain _c.Dispose() invocation for clean chained disposal");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public void Test_MEM02_11_3_1_UnsafeFieldOverwrite_Detected()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("ClassUnsafeFieldOverwrite");
        var classDecl = (ClassDeclarationSyntax)typeDecl;

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
    public void Test_MEM02_11_3_2_PropertyGetterSideEffects_Detected()
    {
        var allocatesInGetter = HasSyntaxInMember<PropertyDeclarationSyntax, ObjectCreationExpressionSyntax>(
            "ClassPropertyGetterSideEffect",
            p => p.Identifier.Text == "Context");

        Assert.True(allocatesInGetter, "Property getter side-effect allocating disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public void Test_MEM02_11_3_2_ImplicitCastOperators_Detected()
    {
        var allocatesInCast = HasSyntaxInMember<ConversionOperatorDeclarationSyntax, ObjectCreationExpressionSyntax>(
            "MyWrapper",
            c => c.ImplicitOrExplicitKeyword.Text == "implicit");

        Assert.True(allocatesInCast, "Implicit cast operator allocating disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public void Test_MEM02_11_3_3_UnguardedMultiAllocation_Detected()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("ClassUnguardedMultiAllocation");
        var classDecl = (ClassDeclarationSyntax)typeDecl;

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
    public void Test_MEM02_11_3_3_FactoryPattern_Validated()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("ClassFactoryPattern");
        var classDecl = (ClassDeclarationSyntax)typeDecl;

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
    public void Test_MEM02_11_3_3_StaticLifecycle_Detected()
    {
        var isDisposableAllocation = HasSyntaxInMember<FieldDeclarationSyntax, ObjectCreationExpressionSyntax>(
            "GpuCache",
            f => f.Modifiers.Any(m => m.Text == "static"));

        Assert.True(isDisposableAllocation, "Static field allocation of disposable resource must be detected");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_11_3_4_StandardInjectionBorrowing_HazardDetected()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("BorrowedResourceClass");
        var classDecl = (ClassDeclarationSyntax)typeDecl;

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
    public void Test_MEM02_11_3_4_ExplicitOwnershipTransfer_Validated()
    {
        var (tree, model, classSymbol, typeDecl) = GetParsedModel("OwnedResourceClass");
        var classDecl = (ClassDeclarationSyntax)typeDecl;

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
    public void Test_MEM02_11_3_5_DefaultInterfaceMethods_Detected()
    {
        var (_, _, _, typeDecl) = GetParsedModel("IResourceBlackbox");
        var dimMethod = typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Process");

        var hasBody = dimMethod.Body != null || dimMethod.ExpressionBody != null;
        var disposesInDIM = HasSyntaxInMember<MethodDeclarationSyntax, InvocationExpressionSyntax>(
            "IResourceBlackbox",
            m => m.Identifier.Text == "Process",
            inv => inv.ToString().Contains("Dispose"));

        Assert.True(hasBody, "Interface method should be a Default Interface Method (DIM) with body");
        Assert.True(disposesInDIM, "DIM taking ownership/disposing parameter must be detected");
    }
}

