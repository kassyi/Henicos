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
        namedType.GetMembers()
            .AsStackEnumerable()
            .Select(GetDisposableInstanceMember)
            .OfType<ISymbol>()
            .AddTo(disposableMembers);

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

    // An instance field, or the property behind a backing field, whose type is disposable; null for any other member
    private static ISymbol? GetDisposableInstanceMember(ISymbol member) =>
        member switch
        {
            IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } f
                when DisposableAnalyzerHelper.IsDisposable(f.Type) => f,
            IFieldSymbol
            {
                IsStatic: false, IsImplicitlyDeclared: true,
                AssociatedSymbol: IPropertySymbol { IsStatic: false } p
            } when DisposableAnalyzerHelper.IsDisposable(p.Type) => p,
            _ => null
        };

    private readonly record struct OwnershipValidationContext(
        SymbolAnalysisContext AnalysisContext,
        INamedTypeSymbol NamedType,
        IReadOnlyList<ISymbol> DisposableMembers,
        HashSet<string> BorrowedFields);

    private static void CollectBorrowedParameterNames(INamedTypeSymbol namedType, HashSet<string> borrowedParamNames) =>
        namedType.Constructors
            .AsStackEnumerable()
            .SelectMany(static ctor => ctor.Parameters
                .AsStackEnumerable()
            ).Where(static p => DisposableAnalyzerHelper.IsDisposable(p.Type) && !HasTakesOwnershipAttribute(p))
            .Select(static p => p.Name)
            .AddTo(borrowedParamNames);

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

        borrowedParamNames
            .AsStackEnumerable()
            .Do(borrowedFields, static (p, fields) =>
                {
                    fields.Add(p);
                    fields.Add("_" + p);
                }
            );

        // Direct lookup via symbol constructors reduces member search from O(Members) to O(Constructors).
        namedType.Constructors
            .AsStackEnumerable()
            .SelectMany(static ctor => ctor.DeclaringSyntaxReferences
                .AsStackEnumerable()
            ).Select(static r => r.GetSyntax())
            .OfType<ConstructorDeclarationSyntax>()
            .Select(static ctorDecl => (SyntaxNode?)ctorDecl.Body ?? ctorDecl.ExpressionBody)
            .OfType<SyntaxNode>()
            // Prune traversal into nested scopes (local functions, lambdas, type declarations) to minimize worst-case
            // AST exploration.
            .SelectMany(static root => root
                .DescendantNodes(descendIntoChildren: static n =>
                    n is not (TypeDeclarationSyntax or LocalFunctionStatementSyntax
                        or AnonymousFunctionExpressionSyntax)
                ).AsStackEnumerable()
            ).OfType<AssignmentExpressionSyntax>()
            .Select(borrowedParamNames, GetBorrowedAssignmentTarget)
            .OfType<string>()
            .AddTo(borrowedFields);
    }

    // The member that a constructor assigns a borrowed parameter to (x = p, this.x = p); null for any other assignment
    private static string? GetBorrowedAssignmentTarget(
        AssignmentExpressionSyntax assign,
        HashSet<string> borrowedParamNames)
    {
        if (assign.Right is not IdentifierNameSyntax id || !borrowedParamNames.Contains(id.Identifier.Text))
        {
            return null;
        }

        return assign.Left switch
        {
            IdentifierNameSyntax idLeft => idLeft.Identifier.Text,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } maLeft => maLeft.Name.Identifier.Text,
            _ => null
        };
    }

    private static void ValidateMissingIDisposable(
        in OwnershipValidationContext ctx,
        bool implementsIDisposable)
    {
        if (implementsIDisposable)
        {
            return;
        }

        var hasOwnedDisposableField = ctx.DisposableMembers
            .AsStackEnumerable()
            .Any(ctx.BorrowedFields, static (member, borrowed) => !borrowed.Contains(member.Name));

        var hasTakesOwnership = ctx.NamedType.Constructors
            .AsStackEnumerable()
            .Any(static ctor => ctor.Parameters
                .AsStackEnumerable()
                .Any(HasTakesOwnershipAttribute)
            );

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

        var root = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        var disposes = root != null &&
            root.DescendantNodes()
                .AsStackEnumerable()
                .Any(static node => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" }
                });

        if (disposes)
        {
            context.ReportDiagnostic(GSharpDiagnostics.DisposableInDim, method.Identifier.GetLocation());
        }
    }
}
