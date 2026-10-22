namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DisposableOwnershipAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.MissingIDisposable,
            GSharpDiagnostics.UndisposedGpuScope,
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
    private static HashSet<string>? s_tBorrowedParamNames;

    [ThreadStatic]
    private static HashSet<string>? s_tBorrowedFields;

    [ThreadStatic]
    private static HashSet<string>? s_tDisposedMemberNames;

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;
        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
        {
            return;
        }

        var disposableMembers = ImmutableArray.CreateBuilder<ISymbol>();

        foreach (var m in namedType.GetMembers())
        {
            var (memberSymbol, type) = m switch
            {
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } f => ((ISymbol)f, f.Type),
                IFieldSymbol
                {
                    IsStatic: false, IsImplicitlyDeclared: true,
                    AssociatedSymbol: IPropertySymbol { IsStatic: false } p
                } => (p, p.Type),
                _ => (null, null)
            };

            if (memberSymbol != null && type != null && DisposableAnalyzerHelper.IsDisposable(type))
            {
                disposableMembers.Add(memberSymbol);
            }
        }

        if (disposableMembers.Count == 0)
        {
            return;
        }

        var implementsIDisposable = DisposableAnalyzerHelper.IsDisposable(namedType);

        var borrowedParamNames = s_tBorrowedParamNames ??= new HashSet<string>(StringComparer.Ordinal);
        borrowedParamNames.Clear();
        CollectBorrowedParameterNames(namedType, borrowedParamNames);

        var borrowedFields = s_tBorrowedFields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        borrowedFields.Clear();
        CollectBorrowedFieldNames(namedType, borrowedParamNames, borrowedFields);

        var validationCtx = new OwnershipValidationContext(context, namedType, disposableMembers, borrowedFields);
        ValidateMissingIDisposable(in validationCtx, implementsIDisposable);

        if (!implementsIDisposable)
        {
            return;
        }

        var disposedMemberNames = s_tDisposedMemberNames ??= new HashSet<string>(StringComparer.Ordinal);
        disposedMemberNames.Clear();
        ValidateDisposeImplementation(in validationCtx, disposedMemberNames);
    }

    private readonly record struct OwnershipValidationContext(
        SymbolAnalysisContext AnalysisContext,
        INamedTypeSymbol NamedType,
        IReadOnlyList<ISymbol> DisposableMembers,
        HashSet<string> BorrowedFields);

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

    private static bool HasTakesOwnershipAttribute(IParameterSymbol param) =>
        param.HasAttribute(KnownAttributes.TakesOwnership);

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

        // Direct lookup via symbol constructors reduces member search from O(Members) to O(Constructors).
        foreach (var ctor in namedType.Constructors)
        {
            foreach (var r in ctor.DeclaringSyntaxReferences)
            {
                if (r.GetSyntax() is not ConstructorDeclarationSyntax ctorDecl)
                {
                    continue;
                }

                var root = (SyntaxNode?)ctorDecl.Body ?? ctorDecl.ExpressionBody;
                if (root == null)
                {
                    continue;
                }

                // Prune traversal into nested scopes (local functions, lambdas, type declarations) to minimize worst-case AST exploration.
                foreach (var node in root.DescendantNodes(descendIntoChildren: static n =>
                             n is not (TypeDeclarationSyntax or LocalFunctionStatementSyntax
                                 or AnonymousFunctionExpressionSyntax)))
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
                        _ => null
                    };

                    if (leftName != null)
                    {
                        borrowedFields.Add(leftName);
                    }
                }
            }
        }
    }

    private static void ValidateMissingIDisposable(
        in OwnershipValidationContext ctx,
        bool implementsIDisposable)
    {
        if (implementsIDisposable)
        {
            return;
        }

        var hasOwnedDisposableField = false;
        foreach (var member in ctx.DisposableMembers)
        {
            if (ctx.BorrowedFields.Contains(member.Name))
            {
                continue;
            }

            hasOwnedDisposableField = true;
            break;
        }

        var hasTakesOwnership = false;
        foreach (var ctor in ctx.NamedType.Constructors)
        {
            foreach (var p in ctor.Parameters)
            {
                if (!HasTakesOwnershipAttribute(p))
                {
                    continue;
                }

                hasTakesOwnership = true;
                break;
            }

            if (hasTakesOwnership)
            {
                break;
            }
        }

        if (!hasOwnedDisposableField && !hasTakesOwnership)
        {
            return;
        }

        var location = ctx.NamedType.Locations.Length > 0 ? ctx.NamedType.Locations[0] : null;
        ctx.AnalysisContext.ReportDiagnostic(GSharpDiagnostics.MissingIDisposable, location);
    }

    private static void ValidateDisposeImplementation(
        in OwnershipValidationContext ctx,
        HashSet<string> disposedMemberNames)
    {
        InspectDisposeMethod(ctx.NamedType, disposedMemberNames, out var parameterlessDispose);

        var locationForDispose = parameterlessDispose?.Identifier.GetLocation() ??
                                 (ctx.NamedType.Locations.Length > 0 ? ctx.NamedType.Locations[0] : null);

        foreach (var member in ctx.DisposableMembers)
        {
            var isBorrowed = ctx.BorrowedFields.Contains(member.Name);
            var isDisposed = disposedMemberNames.Contains(member.Name);

            if (isBorrowed && isDisposed)
            {
                ctx.AnalysisContext.ReportDiagnostic(GSharpDiagnostics.DisposingBorrowedResource, locationForDispose);
            }
            else if (!isBorrowed && !isDisposed && parameterlessDispose != null)
            {
                ctx.AnalysisContext.ReportDiagnostic(GSharpDiagnostics.UndisposedGpuScope, locationForDispose,
                    member.Name);
            }
        }
    }

    private static void InspectDisposeMethod(
        INamedTypeSymbol namedType,
        HashSet<string> disposedMemberNames,
        out MethodDeclarationSyntax? parameterlessDispose)
    {
        parameterlessDispose = null;

        // Use symbol-based index lookup to locate Dispose methods in O(1), eliminating linear scan over all type members.
        foreach (var member in namedType.GetMembers("Dispose"))
        {
            if (member is IMethodSymbol method)
            {
                InspectMethod(method, ref parameterlessDispose, disposedMemberNames);
            }
        }

        // Locate explicit interface implementations (IDisposable.Dispose).
        foreach (var iface in namedType.AllInterfaces)
        {
            if (!DisposableAnalyzerHelper.IsSystemIDisposable(iface))
            {
                continue;
            }

            foreach (var ifaceMember in iface.GetMembers("Dispose"))
            {
                if (namedType.FindImplementationForInterfaceMember(ifaceMember) is IMethodSymbol
                    {
                        MethodKind: MethodKind.ExplicitInterfaceImplementation
                    } implMethod)
                {
                    InspectMethod(implMethod, ref parameterlessDispose, disposedMemberNames);
                }
            }
        }
    }

    private static void InspectMethod(
        IMethodSymbol methodSymbol,
        ref MethodDeclarationSyntax? parameterlessDispose,
        HashSet<string> disposedMemberNames)
    {
        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not MethodDeclarationSyntax m)
            {
                continue;
            }

            if (parameterlessDispose == null && m.ParameterList.Parameters.Count == 0)
            {
                parameterlessDispose = m;
            }

            var root = (SyntaxNode?)m.Body ?? m.ExpressionBody;
            if (root == null)
            {
                continue;
            }

            // Prune traversal into independent scopes (local functions, lambdas, nested types) to minimize search complexity.
            foreach (var node in root.DescendantNodes(descendIntoChildren: static n =>
                         n is not (TypeDeclarationSyntax or LocalFunctionStatementSyntax
                             or AnonymousFunctionExpressionSyntax)))
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

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
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
            context.ReportDiagnostic(GSharpDiagnostics.DisposableInDim, method.Identifier.GetLocation());
        }
    }
}
