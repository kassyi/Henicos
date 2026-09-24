namespace Kassyi.GSharp.Compiler.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DisposableOwnershipAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.MissingIDisposable,
            GSharpDiagnostics.UndisposedField,
            GSharpDiagnostics.DisposingBorrowedResource,
            GSharpDiagnostics.DisposableInDim
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    [ThreadStatic]
    private static List<ISymbol>? t_disposableMembers;

    [ThreadStatic]
    private static HashSet<string>? t_borrowedParamNames;

    [ThreadStatic]
    private static HashSet<string>? t_borrowedFields;

    [ThreadStatic]
    private static HashSet<string>? t_disposedMemberNames;

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;
        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
        {
            return;
        }

        var disposableMembers = t_disposableMembers ??= new List<ISymbol>();
        disposableMembers.Clear();

        foreach (var m in namedType.GetMembers())
        {
            if ((m is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } f &&
                 DisposableAnalyzerHelper.IsDisposable(f.Type)) ||
                (m is IPropertySymbol { IsStatic: false } p && DisposableAnalyzerHelper.IsDisposable(p.Type)))
            {
                disposableMembers.Add(m);
            }
        }

        if (disposableMembers.Count == 0)
        {
            return;
        }

        var implementsIDisposable = DisposableAnalyzerHelper.IsDisposable(namedType);

        var borrowedParamNames = t_borrowedParamNames ??= new HashSet<string>(StringComparer.Ordinal);
        borrowedParamNames.Clear();
        CollectBorrowedParameterNames(namedType, borrowedParamNames);

        var borrowedFields = t_borrowedFields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        borrowedFields.Clear();
        CollectBorrowedFieldNames(namedType, borrowedParamNames, borrowedFields);

        ValidateMissingIDisposable(context, namedType, disposableMembers, borrowedFields, implementsIDisposable);

        if (implementsIDisposable)
        {
            var disposedMemberNames = t_disposedMemberNames ??= new HashSet<string>(StringComparer.Ordinal);
            disposedMemberNames.Clear();
            ValidateDisposeImplementation(context, namedType, disposableMembers, borrowedFields, disposedMemberNames);
        }
    }

    private static void CollectBorrowedParameterNames(INamedTypeSymbol namedType, HashSet<string> borrowedParamNames)
    {
        foreach (var ctor in namedType.Constructors)
        {
            foreach (var p in ctor.Parameters)
            {
                if (DisposableAnalyzerHelper.IsDisposable(p.Type) && !HasTakesOwnershipAttribute(p))
                {
                    borrowedParamNames.Add(p.Name);
                }
            }
        }
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

    private static void CollectBorrowedFieldNames(
        INamedTypeSymbol namedType,
        HashSet<string> borrowedParamNames,
        HashSet<string> borrowedFields)
    {
        if (borrowedParamNames.Count == 0)
        {
            return;
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

        if (!hasOwnedDisposableField)
        {
            return;
        }

        var location = namedType.Locations.Length > 0 ? namedType.Locations[0] : null;
        context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.MissingIDisposable, location));
    }

    private static void ValidateDisposeImplementation(
        SymbolAnalysisContext context,
        INamedTypeSymbol namedType,
        List<ISymbol> disposableMembers,
        HashSet<string> borrowedFields,
        HashSet<string> disposedMemberNames)
    {
        InspectDisposeMethod(namedType, disposedMemberNames, out var parameterlessDispose);

        var locationForDispose = parameterlessDispose?.Identifier.GetLocation() ??
                                 (namedType.Locations.Length > 0 ? namedType.Locations[0] : null);

        foreach (var member in disposableMembers)
        {
            var isBorrowed = borrowedFields.Contains(member.Name);
            var isDisposed = disposedMemberNames.Contains(member.Name);

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
        HashSet<string> disposedMemberNames,
        out MethodDeclarationSyntax? parameterlessDispose)
    {
        parameterlessDispose = null;

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
                        disposedMemberNames.Add(targetName);
                    }
                }
            }
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
}
