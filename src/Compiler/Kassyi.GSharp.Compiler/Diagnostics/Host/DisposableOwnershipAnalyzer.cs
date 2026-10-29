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

        ValidateDisposeImplementation(in validationCtx);
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

    private static void ValidateDisposeImplementation(in OwnershipValidationContext ctx)
    {
        var disposeBodies = ImmutableArray.CreateBuilder<SyntaxNode>();
        InspectDisposeMethod(ctx.NamedType, disposeBodies, out var parameterlessDispose);

        var locationForDispose = parameterlessDispose?.Identifier.GetLocation() ??
                                 (ctx.NamedType.Locations.Length > 0 ? ctx.NamedType.Locations[0] : null);

        foreach (var member in ctx.DisposableMembers)
        {
            var release = GetRelease(disposeBodies, ctx.NamedType, member.Name);
            var isBorrowed = ctx.BorrowedFields.Contains(member.Name);

            // [WHY] GSH0027 needs a certain release (an escaped field may only be logged); GSH0022 reports only a field
            // that no path can release, so that a suspicion never rejects correct code (semantics charter §3)
            if (isBorrowed && release == FieldRelease.Disposed)
            {
                ctx.AnalysisContext.ReportDiagnostic(GSharpDiagnostics.DisposingBorrowedResource, locationForDispose);
            }
            else if (!isBorrowed && release == FieldRelease.None && parameterlessDispose != null)
            {
                ctx.AnalysisContext.ReportDiagnostic(GSharpDiagnostics.UndisposedGpuScope, locationForDispose,
                    member.Name);
            }
        }
    }

    private static void InspectDisposeMethod(
        INamedTypeSymbol namedType,
        ImmutableArray<SyntaxNode>.Builder disposeBodies,
        out MethodDeclarationSyntax? parameterlessDispose)
    {
        parameterlessDispose = null;

        // Use symbol-based index lookup to locate Dispose methods in O(1), eliminating linear scan over all type members.
        foreach (var member in namedType.GetMembers("Dispose"))
        {
            if (member is IMethodSymbol method)
            {
                InspectMethod(method, ref parameterlessDispose, disposeBodies);
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
                    InspectMethod(implMethod, ref parameterlessDispose, disposeBodies);
                }
            }
        }
    }

    private static void InspectMethod(
        IMethodSymbol methodSymbol,
        ref MethodDeclarationSyntax? parameterlessDispose,
        ImmutableArray<SyntaxNode>.Builder disposeBodies)
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

            if (((SyntaxNode?)m.Body ?? m.ExpressionBody) is { } root)
            {
                disposeBodies.Add(root);
            }
        }
    }

    /// <summary>How surely a body releases a field or a parameter (ordered: a stronger result wins).</summary>
    private enum FieldRelease
    {
        /// <summary>No path disposes it.</summary>
        None,

        /// <summary>It is passed to a method whose body the analysis does not follow, which may release it.</summary>
        Escaped,

        /// <summary>A path disposes it (<c>x.Dispose()</c> or <c>x?.Dispose()</c>, directly or in a helper).</summary>
        Disposed
    }

    // How many helpers deep a field is followed, as CA2213 does (DefaultMaxInterproceduralMethodCallChain = 3); a
    // helper deeper than that counts as Escaped
    private const int MaxHelperDepth = 3;

    private static FieldRelease GetRelease(ImmutableArray<SyntaxNode>.Builder disposeBodies, INamedTypeSymbol type,
        string fieldName)
    {
        var visited = new Dictionary<(SyntaxNode Body, string Name), int>();
        var release = FieldRelease.None;
        foreach (var body in disposeBodies)
        {
            release = Max(release, GetRelease(body, body, fieldName, isField: true, type, depth: 0, visited));
        }

        return release;
    }

    // Whether <body> releases <name>: a field of <type> (also written this.<name>) or a parameter of the helper whose
    // body this is. A call to a helper of the type (a method with a body, or a local function of <member>, the body of
    // the method that declares <body>) is followed for each argument that is <name> (to the matching parameter) and,
    // for a field, into the helper's body itself. Lambdas and nested types are not searched: they may never run.
    // [WHY] <visited> keeps the smallest depth each body was walked at for <name>: a body reached first on a deep path
    // is walked again from a shallower one, which may follow its helpers further. A cycle stops there with None.
    private static FieldRelease GetRelease(SyntaxNode body, SyntaxNode member, string name, bool isField,
        INamedTypeSymbol type, int depth, Dictionary<(SyntaxNode Body, string Name), int> visited)
    {
        if (visited.TryGetValue((body, name), out var seen) && seen <= depth)
        {
            return FieldRelease.None;
        }

        visited[(body, name)] = depth;

        var release = FieldRelease.None;
        foreach (var node in body.DescendantNodes(descendIntoChildren: static n =>
                     n is not (TypeDeclarationSyntax or LocalFunctionStatementSyntax
                         or AnonymousFunctionExpressionSyntax)))
        {
            if (node is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            if (IsDisposeOf(invocation, name, isField))
            {
                return FieldRelease.Disposed;
            }

            release = Max(release, GetHelperRelease(invocation, member, name, isField, type, depth, visited));
            if (release == FieldRelease.Disposed)
            {
                return release;
            }
        }

        return release;
    }

    private static FieldRelease GetHelperRelease(InvocationExpressionSyntax invocation, SyntaxNode member, string name,
        bool isField, INamedTypeSymbol type, int depth, Dictionary<(SyntaxNode Body, string Name), int> visited)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var passedAt = arguments
            .AsStackEnumerable()
            .IndexOf((Name: name, IsField: isField), static (argument, target) =>
                !argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) &&
                RefersTo(argument.Expression, target.Name, target.IsField)
            );

        var helpers = GetHelpers(invocation, member, type);
        if (helpers.Count == 0)
        {
            // nameof(<name>) only names it; a method the analysis cannot see (another type, a base type, a delegate)
            // may release what it is given
            return passedAt >= 0 && !IsNameOf(invocation) ? FieldRelease.Escaped : FieldRelease.None;
        }

        if (passedAt < 0 && !isField)
        {
            return FieldRelease.None;
        }

        if (depth >= MaxHelperDepth)
        {
            return FieldRelease.Escaped;
        }

        var release = FieldRelease.None;
        foreach (var (helperBody, helperMember, parameters) in helpers)
        {
            if (isField)
            {
                release = Max(release,
                    GetRelease(helperBody, helperMember, name, isField: true, type, depth + 1, visited));
            }

            if (passedAt >= 0 && GetParameterName(arguments[passedAt], passedAt, parameters) is { } parameter)
            {
                release = Max(release,
                    GetRelease(helperBody, helperMember, parameter, isField: false, type, depth + 1, visited));
            }
        }

        return release;
    }

    // The bodies of the helpers that <invocation> may call, the member bodies that declare them and their parameters:
    // the methods of <type> with its name (every overload, since the syntax alone cannot pick one) and the local
    // functions of <member>, the body of the method the call is written in (a local function may call another one)
    private static List<(SyntaxNode Body, SyntaxNode Member, ParameterListSyntax Parameters)> GetHelpers(
        InvocationExpressionSyntax invocation, SyntaxNode member, INamedTypeSymbol type)
    {
        var helpers = new List<(SyntaxNode Body, SyntaxNode Member, ParameterListSyntax Parameters)>();
        var name = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access => access.Name.Identifier.Text,
            _ => null
        };
        if (name == null)
        {
            return helpers;
        }

        foreach (var candidate in type.GetMembers(name))
        {
            if (candidate is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                continue;
            }

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                    ((SyntaxNode?)declaration.Body ?? declaration.ExpressionBody) is { } helperBody)
                {
                    helpers.Add((helperBody, helperBody, declaration.ParameterList));
                }
            }
        }

        // A local function is called by its name alone
        if (invocation.Expression is IdentifierNameSyntax or GenericNameSyntax)
        {
            member.DescendantNodes(descendIntoChildren: static n =>
                    n is not (TypeDeclarationSyntax or AnonymousFunctionExpressionSyntax)
                ).AsStackEnumerable()
                .OfType<LocalFunctionStatementSyntax>()
                .Where(name, static (local, target) => local.Identifier.Text == target)
                .Select(member, static (local, owner) => (GetLocalFunctionBody(local), owner, local.ParameterList))
                .AddTo(helpers);
        }

        return helpers;
    }

    // The body of a local function; an extern one has none, so its declaration (which holds no call) stands for it
    private static SyntaxNode GetLocalFunctionBody(LocalFunctionStatementSyntax local) =>
        (SyntaxNode?)local.Body ?? local.ExpressionBody ?? (SyntaxNode)local;

    // nameof(...): the caller checks first that no helper has that name, so the call is the operator
    private static bool IsNameOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" };

    // The parameter that <argument> (at <position>) binds to: by its name when it has one, otherwise by position
    private static string? GetParameterName(ArgumentSyntax argument, int position, ParameterListSyntax parameters)
    {
        if (argument.NameColon is { } nameColon)
        {
            return nameColon.Name.Identifier.Text;
        }

        return position < parameters.Parameters.Count ? parameters.Parameters[position].Identifier.Text : null;
    }

    // <name>.Dispose() or <name>?.Dispose()
    private static bool IsDisposeOf(InvocationExpressionSyntax invocation, string name, bool isField) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispose" } access =>
                RefersTo(access.Expression, name, isField),
            MemberBindingExpressionSyntax { Name.Identifier.Text: "Dispose" } =>
                invocation.Parent is ConditionalAccessExpressionSyntax conditional &&
                conditional.WhenNotNull == invocation &&
                RefersTo(conditional.Expression, name, isField),
            _ => false
        };

    // <name>, or this.<name> for a field
    private static bool RefersTo(ExpressionSyntax expression, string name, bool isField) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == name,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access =>
                isField && access.Name.Identifier.Text == name,
            _ => false
        };

    private static FieldRelease Max(FieldRelease a, FieldRelease b) => a > b ? a : b;

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
