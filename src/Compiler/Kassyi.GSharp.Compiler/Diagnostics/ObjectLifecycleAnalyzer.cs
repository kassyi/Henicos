using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ObjectLifecycleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.MissingIDisposable,
            GSharpDiagnostics.UndisposedField,
            GSharpDiagnostics.UnsafeFieldOverwrite,
            GSharpDiagnostics.PropertyGetterSideEffect,
            GSharpDiagnostics.CastOperatorSideEffect,
            GSharpDiagnostics.UnguardedMultiAllocation,
            GSharpDiagnostics.StaticDisposableAllocation,
            GSharpDiagnostics.DisposingBorrowedResource,
            GSharpDiagnostics.DisposableInDim,
            GSharpDiagnostics.ManualUnmanagedAllocation
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConversionOperator, SyntaxKind.ConversionOperatorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static bool IsDisposable(ITypeSymbol? type)
    {
        if (type == null) return false;
        return type.AllInterfaces.Any(i => i.Name == "IDisposable");
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;
        
        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
            return;

        bool implementsIDisposable = IsDisposable(namedType);
        
        var disposableMembers = namedType.GetMembers()
            .Where(m => (m is IFieldSymbol f && !f.IsStatic && !f.IsImplicitlyDeclared && IsDisposable(f.Type)) ||
                        (m is IPropertySymbol p && !p.IsStatic && IsDisposable(p.Type)))
            .ToList();

        if (!disposableMembers.Any()) return;

        // Track borrowed parameters (injected without [TakesOwnership])
        var borrowedParamNames = namedType.Constructors.SelectMany(c => c.Parameters)
            .Where(p => IsDisposable(p.Type) && !p.GetAttributes().Any(a => a.AttributeClass?.Name == "TakesOwnershipAttribute"))
            .Select(p => p.Name).ToList();

        var borrowedFields = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in borrowedParamNames)
        {
            borrowedFields.Add(p);
            borrowedFields.Add("_" + p);
        }

        // Add explicitly assigned fields in constructors
        foreach (var ctor in namedType.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().SelectMany(c => c.Members.OfType<ConstructorDeclarationSyntax>()))
        {
            if (ctor.Body == null) continue;
            var assignments = ctor.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
            foreach (var assign in assignments)
            {
                if (assign.Right is IdentifierNameSyntax id && borrowedParamNames.Contains(id.Identifier.Text))
                {
                    string leftName = "";
                    if (assign.Left is IdentifierNameSyntax idLeft) leftName = idLeft.Identifier.Text;
                    else if (assign.Left is MemberAccessExpressionSyntax maLeft && maLeft.Expression is ThisExpressionSyntax) leftName = maLeft.Name.Identifier.Text;
                    
                    if (!string.IsNullOrEmpty(leftName)) borrowedFields.Add(leftName);
                }
            }
        }

        bool hasOwnedDisposableField = disposableMembers.Any(m => !borrowedFields.Contains(m.Name));

        if (hasOwnedDisposableField && !implementsIDisposable)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.MissingIDisposable, namedType.Locations.FirstOrDefault()));
        }

        if (implementsIDisposable)
        {
            // Gather all methods named Dispose (handles Dispose() and Dispose(bool))
            var typeDecls = namedType.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>();
            var disposeMethods = typeDecls.SelectMany(t => t.Members.OfType<MethodDeclarationSyntax>())
                                          .Where(m => m.Identifier.Text == "Dispose").ToList();

            var parameterlessDispose = disposeMethods.FirstOrDefault(m => m.ParameterList.Parameters.Count == 0);
            
            var disposeInvocations = disposeMethods.SelectMany(m => m.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Select(inv => inv.Expression as MemberAccessExpressionSyntax)
                .Where(ma => ma != null && ma.Name.Identifier.Text == "Dispose")
                .ToList();

            foreach (var member in disposableMembers)
            {
                bool isBorrowed = borrowedFields.Contains(member.Name);
                bool isDisposed = disposeInvocations.Any(ma => 
                    (ma!.Expression is IdentifierNameSyntax id && id.Identifier.Text == member.Name) ||
                    (ma.Expression is MemberAccessExpressionSyntax memberMa && memberMa.Expression is ThisExpressionSyntax && memberMa.Name.Identifier.Text == member.Name));

                var location = parameterlessDispose?.Identifier.GetLocation() ?? namedType.Locations.FirstOrDefault();

                if (isBorrowed && isDisposed)
                {
                    context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DisposingBorrowedResource, location));
                }
                else if (!isBorrowed && !isDisposed && parameterlessDispose != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UndisposedField, location));
                }
            }
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, property.ExpressionBody.Expression, GSharpDiagnostics.PropertyGetterSideEffect);
        }
        else if (property.AccessorList != null)
        {
            var getter = property.AccessorList.Accessors.FirstOrDefault(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);
            if (getter != null)
            {
                if (getter.ExpressionBody != null)
                {
                    CheckForAllocationInExpression(context, getter.ExpressionBody.Expression, GSharpDiagnostics.PropertyGetterSideEffect);
                }
                else if (getter.Body != null)
                {
                    CheckForAllocationInBlock(context, getter.Body, GSharpDiagnostics.PropertyGetterSideEffect);
                }
            }
        }
    }

    private static void AnalyzeConversionOperator(SyntaxNodeAnalysisContext context)
    {
        var operatorDecl = (ConversionOperatorDeclarationSyntax)context.Node;
        if (operatorDecl.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, operatorDecl.ExpressionBody.Expression, GSharpDiagnostics.CastOperatorSideEffect);
        }
        else if (operatorDecl.Body != null)
        {
            CheckForAllocationInBlock(context, operatorDecl.Body, GSharpDiagnostics.CastOperatorSideEffect);
        }
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsStatic && IsDisposable(field.Type))
        {
            var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
            if (syntax?.Initializer?.Value is ObjectCreationExpressionSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.StaticDisposableAllocation, syntax.GetLocation()));
            }
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctor = (ConstructorDeclarationSyntax)context.Node;
        if (ctor.Body == null) return;

        var allocations = ctor.Body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(oce => IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            .ToList();

        if (allocations.Count >= 2)
        {
            var tryStatements = ctor.Body.DescendantNodes().OfType<TryStatementSyntax>().ToList();
            if (!tryStatements.Any())
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnguardedMultiAllocation, ctor.Identifier.GetLocation()));
            }
        }
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null) return;

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface && 
            (method.Body != null || method.ExpressionBody != null))
        {
            var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Dispose");
            if (disposes)
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DisposableInDim, method.Identifier.GetLocation()));
            }
        }
    }


    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Right is ObjectCreationExpressionSyntax oce)
        {
            var type = context.SemanticModel.GetTypeInfo(oce).Type;
            if (IsDisposable(type))
            {
                var leftSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (leftSymbol is IFieldSymbol fieldSymbol)
                {
                    var enclosingMethod = assignment.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    if (enclosingMethod != null)
                    {
                        var priorDispose = enclosingMethod.DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Where(inv => inv.SpanStart < assignment.SpanStart &&
                                          inv.Expression is MemberAccessExpressionSyntax ma && 
                                          ma.Name.Identifier.Text == "Dispose" &&
                                          ((ma.Expression is IdentifierNameSyntax id && id.Identifier.Text == fieldSymbol.Name) ||
                                           (ma.Expression is MemberAccessExpressionSyntax fieldMa && fieldMa.Expression is ThisExpressionSyntax && fieldMa.Name.Identifier.Text == fieldSymbol.Name)))
                            .Any();
                            
                        if (!priorDispose)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnsafeFieldOverwrite, assignment.GetLocation()));
                        }
                    }
                }
            }
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.Name == "Malloc" && methodSymbol.ContainingType?.Name == "Unsafe" && methodSymbol.ContainingType?.ContainingType?.Name == "Gpu")
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.ManualUnmanagedAllocation, invocation.GetLocation()));
            }
        }
    }

    private static void CheckForAllocationInExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, DiagnosticDescriptor descriptor)
    {
        var allocations = expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>()
            .Where(oce => IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            .ToList();
        
        foreach (var allocation in allocations)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, allocation.GetLocation()));
        }
    }

    private static void CheckForAllocationInBlock(SyntaxNodeAnalysisContext context, BlockSyntax block, DiagnosticDescriptor descriptor)
    {
        var allocations = block.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(oce => IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            .ToList();

        foreach (var allocation in allocations)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, allocation.GetLocation()));
        }
    }
}
