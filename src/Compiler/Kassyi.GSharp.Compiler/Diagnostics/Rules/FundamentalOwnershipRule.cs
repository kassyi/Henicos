namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>Diagnostic rule that enforces Fundamental Ownership (MEM-01) for GPU resources.</summary>
internal sealed class FundamentalOwnershipRule
{
    public static readonly FundamentalOwnershipRule Instance = new();

    private FundamentalOwnershipRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        var localDecls = node.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(o =>
                o.Type is IdentifierNameSyntax { Identifier.Text: "GpuContext" }));

        // 1. Discarded Allocation
        ReportIf(
            node.DescendantNodes().OfType<ExpressionStatementSyntax>(),
            e => e.Expression is ObjectCreationExpressionSyntax
            {
                Type: IdentifierNameSyntax { Identifier.Text: "GpuContext" }
            },
            e => e.GetLocation(),
            _ => "new GpuContext()",
            "it is instantiated but immediately discarded without being assigned or disposed",
            ref context);

        // 2. Undisposed Local
        var variableDeclaratorSyntaxes = localDecls as VariableDeclaratorSyntax[] ?? [.. localDecls];
        ReportIf(
            variableDeclaratorSyntaxes,
            local => !IsUsing(local) && GetFirstDisposeInvocation(node, local.Identifier.Text) == null,
            local => local.GetLocation(),
            local => local.Identifier.Text,
            "it goes out of scope without being explicitly disposed (missing .Dispose() or 'using' keyword)",
            ref context);

        // 3. Control Flow Bypass
        var bypasses = from ret in node.DescendantNodes().OfType<ReturnStatementSyntax>()
                       from local in variableDeclaratorSyntaxes
                       where !IsUsing(local)
                       let firstDispose = GetFirstDisposeInvocation(node, local.Identifier.Text)
                       where firstDispose != null && ret.SpanStart < firstDispose.SpanStart
                       select new
                       {
                           Location = ret.GetLocation(),
                           Name = local.Identifier.Text
                       };

        ReportIf(bypasses, _ => true, b => b.Location, b => b.Name,
            "a return statement bypasses its disposal, causing a leak", ref context);

        // 4. Async Void
        var isAsync = node.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
        var isVoid = node.ReturnType is PredefinedTypeSyntax pt && pt.Keyword.IsKind(SyntaxKind.VoidKeyword);
        var hasGpuContextParam = node.ParameterList.Parameters
            .Select(p => p.Type)
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.Text == "GpuContext");

        if (isAsync && isVoid && hasGpuContextParam)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.FundamentalOwnershipError,
                node.Identifier.GetLocation(), node.Identifier.Text,
                "it is passed to an 'async void' method, which cannot be safely awaited for disposal tracking"));
        }

        // 5. Unsafe Nullable Disposal
        var nullableLocals = node.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Parent is VariableDeclarationSyntax { Type: NullableTypeSyntax })
            .Select(v => v.Identifier.Text);

        ReportIf(
            node.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            inv => inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } mae &&
                   nullableLocals.Any(nl => nl == id.Identifier.Text) && mae.Name.Identifier.Text == "Dispose",
            inv => inv.GetLocation(),
            inv => ((IdentifierNameSyntax)((MemberAccessExpressionSyntax)inv.Expression).Expression).Identifier.Text,
            "calling .Dispose() directly on a nullable context is unsafe. Use '?.' instead",
            ref context);

        // 6. Null Assignment Without Disposal
        var nullAssignments = from a in node.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                              where a.Left is IdentifierNameSyntax && a.Right.IsKind(SyntaxKind.NullLiteralExpression)
                              let name = ((IdentifierNameSyntax)a.Left).Identifier.Text
                              let firstDispose = GetFirstDisposeInvocation(node, name)
                              where firstDispose == null || a.SpanStart < firstDispose.SpanStart
                              select new
                              {
                                  Location = a.GetLocation(),
                                  Name = name
                              };

        ReportIf(nullAssignments, _ => true, a => a.Location, a => a.Name,
            "it is assigned to null before being properly disposed, causing a leak", ref context);
    }

    private static void ReportIf<T>(
        IEnumerable<T> items,
        Func<T, bool> predicate,
        Func<T, Location> getLocation,
        Func<T, string> getTargetName,
        string message,
        ref ValidationContext context)
    {
        foreach (var item in items)
        {
            if (predicate(item))
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.FundamentalOwnershipError,
                    getLocation(item), getTargetName(item), message));
            }
        }
    }

    private static bool IsUsing(VariableDeclaratorSyntax local) =>
        local.Parent?.Parent is LocalDeclarationStatementSyntax ld && ld.UsingKeyword.IsKind(SyntaxKind.UsingKeyword);

    private static InvocationExpressionSyntax? GetFirstDisposeInvocation(
        MethodDeclarationSyntax node, string varName)
    {
        return node.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                (inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id1 } mae &&
                 id1.Identifier.Text == varName &&
                 mae.Name.Identifier.Text == "Dispose") ||
                (inv.Parent is ConditionalAccessExpressionSyntax { Expression: IdentifierNameSyntax id2 } &&
                 id2.Identifier.Text == varName &&
                 inv.Expression is MemberBindingExpressionSyntax { Name.Identifier.Text: "Dispose" })
            );
    }
}
