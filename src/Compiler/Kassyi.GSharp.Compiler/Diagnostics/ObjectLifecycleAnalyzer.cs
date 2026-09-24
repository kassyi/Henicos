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

    private static bool IsSystemIDisposable(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_IDisposable ||
            type.OriginalDefinition.SpecialType == SpecialType.System_IDisposable)
        {
            return true;
        }

        return type is
        {
            Name: "IDisposable", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        };
    }

    private static bool IsDisposable(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (IsSystemIDisposable(type))
        {
            return true;
        }

        foreach (var i in type.AllInterfaces)
        {
            if (IsSystemIDisposable(i))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;
        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
        {
            return;
        }

        var disposableMembers = CollectDisposableInstanceMembers(namedType);
        if (disposableMembers == null || disposableMembers.Count == 0)
        {
            return;
        }

        var implementsIDisposable = IsDisposable(namedType);
        var borrowedParamNames = CollectBorrowedParameterNames(namedType);
        var borrowedFields = CollectBorrowedFieldNames(namedType, borrowedParamNames);

        ValidateMissingIDisposable(context, namedType, disposableMembers, borrowedFields, implementsIDisposable);

        if (implementsIDisposable)
        {
            ValidateDisposeImplementation(context, namedType, disposableMembers, borrowedFields);
        }
    }

    private static List<ISymbol>? CollectDisposableInstanceMembers(INamedTypeSymbol namedType)
    {
        List<ISymbol>? disposableMembers = null;
        foreach (var m in namedType.GetMembers())
        {
            if ((m is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } f && IsDisposable(f.Type)) ||
                (m is IPropertySymbol { IsStatic: false } p && IsDisposable(p.Type)))
            {
                disposableMembers ??= [];
                disposableMembers.Add(m);
            }
        }

        return disposableMembers;
    }

    private static HashSet<string>? CollectBorrowedParameterNames(INamedTypeSymbol namedType)
    {
        HashSet<string>? borrowedParamNames = null;
        foreach (var ctor in namedType.Constructors)
        {
            foreach (var p in ctor.Parameters)
            {
                if (IsDisposable(p.Type) && !HasTakesOwnershipAttribute(p))
                {
                    borrowedParamNames ??= new HashSet<string>(StringComparer.Ordinal);
                    borrowedParamNames.Add(p.Name);
                }
            }
        }

        return borrowedParamNames;
    }

    private static bool HasTakesOwnershipAttribute(IParameterSymbol param)
    {
        foreach (var a in param.GetAttributes())
        {
            if (a.AttributeClass?.Name == "TakesOwnershipAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CollectBorrowedFieldNames(
        INamedTypeSymbol namedType,
        HashSet<string>? borrowedParamNames)
    {
        var borrowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (borrowedParamNames == null || borrowedParamNames.Count == 0)
        {
            return borrowedFields;
        }

        foreach (var p in borrowedParamNames)
        {
            borrowedFields.Add(p);
            borrowedFields.Add("_" + p);
        }

        foreach (var r in namedType.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is not TypeDeclarationSyntax typeDecl)
            {
                continue;
            }

            foreach (var member in typeDecl.Members)
            {
                if (member is not ConstructorDeclarationSyntax { Body: not null } ctorDecl)
                {
                    continue;
                }

                foreach (var node in ctorDecl.Body.DescendantNodes())
                {
                    if (node is not AssignmentExpressionSyntax assign ||
                        assign.Right is not IdentifierNameSyntax id ||
                        !borrowedParamNames.Contains(id.Identifier.Text))
                    {
                        continue;
                    }

                    var leftName = assign.Left switch
                    {
                        IdentifierNameSyntax idLeft => idLeft.Identifier.Text,
                        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } maLeft => maLeft.Name
                            .Identifier.Text,
                        _ => ""
                    };

                    if (!string.IsNullOrEmpty(leftName))
                    {
                        borrowedFields.Add(leftName);
                    }
                }
            }
        }

        return borrowedFields;
    }

    private static void ValidateMissingIDisposable(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        List<ISymbol> disposableMembers,
        HashSet<string> borrowedFields,
        bool implementsIDisposable)
    {
        if (implementsIDisposable)
        {
            return;
        }

        var hasOwnedDisposableField = false;
        foreach (var member in disposableMembers)
        {
            if (!borrowedFields.Contains(member.Name))
            {
                hasOwnedDisposableField = true;
                break;
            }
        }

        if (hasOwnedDisposableField)
        {
            var location = namedType.Locations.Length > 0 ? namedType.Locations[0] : null;
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.MissingIDisposable, location));
        }
    }

    private static void ValidateDisposeImplementation(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        List<ISymbol> disposableMembers,
        HashSet<string> borrowedFields)
    {
        InspectDisposeMethod(namedType, out var parameterlessDispose, out var disposedMemberNames);

        var locationForDispose = parameterlessDispose?.Identifier.GetLocation() ??
                                 (namedType.Locations.Length > 0 ? namedType.Locations[0] : null);

        foreach (var member in disposableMembers)
        {
            var isBorrowed = borrowedFields.Contains(member.Name);
            var isDisposed = disposedMemberNames != null && disposedMemberNames.Contains(member.Name);

            if (isBorrowed && isDisposed)
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DisposingBorrowedResource,
                    locationForDispose));
            }
            else if (!isBorrowed && !isDisposed && parameterlessDispose != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UndisposedField, locationForDispose));
            }
        }
    }

    private static void InspectDisposeMethod(
        INamedTypeSymbol namedType,
        out MethodDeclarationSyntax? parameterlessDispose,
        out HashSet<string>? disposedMemberNames)
    {
        parameterlessDispose = null;
        disposedMemberNames = null;

        foreach (var r in namedType.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is not TypeDeclarationSyntax typeDecl)
            {
                continue;
            }

            foreach (var member in typeDecl.Members)
            {
                if (member is not MethodDeclarationSyntax { Identifier.Text: "Dispose" } m)
                {
                    continue;
                }

                if (parameterlessDispose == null && m.ParameterList.Parameters.Count == 0)
                {
                    parameterlessDispose = m;
                }

                foreach (var node in m.DescendantNodes())
                {
                    if (node is not InvocationExpressionSyntax
                        {
                            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" } ma
                        })
                    {
                        continue;
                    }

                    var targetName = ma.Expression switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberMa => memberMa.Name
                            .Identifier.Text,
                        _ => null
                    };

                    if (targetName != null)
                    {
                        disposedMemberNames ??= new HashSet<string>(StringComparer.Ordinal);
                        disposedMemberNames.Add(targetName);
                    }
                }
            }
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, property.ExpressionBody.Expression,
                GSharpDiagnostics.PropertyGetterSideEffect);
        }
        else if (property.AccessorList != null)
        {
            AccessorDeclarationSyntax? getter = null;
            foreach (var a in property.AccessorList.Accessors)
            {
                if (a.Kind() != SyntaxKind.GetAccessorDeclaration)
                {
                    continue;
                }

                getter = a;
                break;
            }

            if (getter == null)
            {
                return;
            }

            if (getter.ExpressionBody != null)
            {
                CheckForAllocationInExpression(context, getter.ExpressionBody.Expression,
                    GSharpDiagnostics.PropertyGetterSideEffect);
            }
            else if (getter.Body != null)
            {
                CheckForAllocationInBlock(context, getter.Body, GSharpDiagnostics.PropertyGetterSideEffect);
            }
        }
    }

    private static void AnalyzeConversionOperator(SyntaxNodeAnalysisContext context)
    {
        var operatorDecl = (ConversionOperatorDeclarationSyntax)context.Node;
        if (operatorDecl.ExpressionBody != null)
        {
            CheckForAllocationInExpression(context, operatorDecl.ExpressionBody.Expression,
                GSharpDiagnostics.CastOperatorSideEffect);
        }
        else if (operatorDecl.Body != null)
        {
            CheckForAllocationInBlock(context, operatorDecl.Body, GSharpDiagnostics.CastOperatorSideEffect);
        }
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!field.IsStatic || !IsDisposable(field.Type))
        {
            return;
        }

        var syntax = field.DeclaringSyntaxReferences.Length > 0
            ? field.DeclaringSyntaxReferences[0].GetSyntax() as VariableDeclaratorSyntax
            : null;
        if (syntax?.Initializer?.Value is ObjectCreationExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.StaticDisposableAllocation,
                syntax.GetLocation()));
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctor = (ConstructorDeclarationSyntax)context.Node;
        if (ctor.Body == null)
        {
            return;
        }

        var allocationCount = 0;
        var hasTry = false;

        // Single-pass scan for both allocations and try statements with early-exit
        foreach (var node in ctor.Body.DescendantNodes())
        {
            if (!hasTry && node is TryStatementSyntax)
            {
                hasTry = true;
                if (allocationCount >= 2)
                {
                    break;
                }
            }
            else if (allocationCount < 2 &&
                     node is ObjectCreationExpressionSyntax oce &&
                     IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            {
                allocationCount++;
                if (allocationCount >= 2 && hasTry)
                {
                    break;
                }
            }
        }

        if (allocationCount >= 2 && !hasTry)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnguardedMultiAllocation,
                ctor.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null)
        {
            return;
        }

        if (methodSymbol.ContainingType?.TypeKind != TypeKind.Interface ||
            (method.Body == null && method.ExpressionBody == null))
        {
            return;
        }

        var disposes = false;
        var root = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (root != null)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" }
                    })
                {
                    continue;
                }

                disposes = true;
                break;
            }
        }

        if (disposes)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DisposableInDim,
                method.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Right is not ObjectCreationExpressionSyntax oce)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(oce).Type;
        if (!IsDisposable(type))
        {
            return;
        }

        var leftSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (leftSymbol is not IFieldSymbol fieldSymbol)
        {
            return;
        }

        MethodDeclarationSyntax? enclosingMethod = null;
        for (var parent = assignment.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is not MethodDeclarationSyntax m)
            {
                continue;
            }

            enclosingMethod = m;
            break;
        }

        if (enclosingMethod == null)
        {
            return;
        }

        var priorDispose = false;
        var assignmentSpanStart = assignment.SpanStart;
        foreach (var node in enclosingMethod.DescendantNodes(n => n.SpanStart < assignmentSpanStart))
        {
            if (node is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" } ma
                } inv ||
                inv.SpanStart >= assignmentSpanStart ||
                ((ma.Expression is not IdentifierNameSyntax id || id.Identifier.Text != fieldSymbol.Name) &&
                 (ma.Expression is not MemberAccessExpressionSyntax
                 {
                     Expression: ThisExpressionSyntax
                 } fieldMa ||
                  fieldMa.Name.Identifier.Text != fieldSymbol.Name)))
            {
                continue;
            }

            priorDispose = true;
            break;
        }

        if (!priorDispose)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.UnsafeFieldOverwrite,
                assignment.GetLocation()));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol
            {
                Name: "Malloc",
                ContainingType: { Name: "Unsafe", ContainingType.Name: "Gpu" }
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.ManualUnmanagedAllocation,
                invocation.GetLocation()));
        }
    }

    private static void CheckForAllocationInExpression(SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        DiagnosticDescriptor descriptor)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is ObjectCreationExpressionSyntax oce &&
                IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, oce.GetLocation()));
            }
        }
    }

    private static void CheckForAllocationInBlock(SyntaxNodeAnalysisContext context, BlockSyntax block,
        DiagnosticDescriptor descriptor)
    {
        foreach (var node in block.DescendantNodes())
        {
            if (node is ObjectCreationExpressionSyntax oce &&
                IsDisposable(context.SemanticModel.GetTypeInfo(oce).Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, oce.GetLocation()));
            }
        }
    }
}
