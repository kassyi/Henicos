using Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes a <c>using</c> of the CPU dialect as C# lowers it, a <c>try</c> with a <c>finally</c> that disposes the
/// resource (Roslyn LocalRewriter_UsingStatement), with the <c>finally</c> written out.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// T r = &lt;init&gt;;
/// try { &lt;body&gt; [goto __gsharp_using_done_&lt;n&gt;;] }
/// catch (...) { int __gsharp_using_outer = __gsharp_using_enter(); try { r.__gsharp_dispose(); }
///   catch (...) { __gsharp_using_dispose_failed(); } __gsharp_using_leave(__gsharp_using_outer); throw; }
/// [__gsharp_using_exit_&lt;n&gt;_&lt;j&gt;: r.__gsharp_dispose(); &lt;the jump&gt;] ...
/// [__gsharp_using_done_&lt;n&gt;:] r.__gsharp_dispose();
/// </code>
/// </para>
/// <para>
/// [WHY] On the CPU a failed check ends the step with an exception (ErrorChannel.hpp). A guard whose destructor
/// disposes would run <c>Dispose</c> while that exception unwinds, where a failure of <c>Dispose</c> may not leave the
/// destructor (std::terminate), and telling the two cases apart cost the normal path a flag or a test. Here the
/// <c>catch</c> disposes on a failure, and the normal path is the body followed by the call of <c>Dispose</c>. A jump
/// out of the body (<see cref="UsingExitPlan"/>) goes to an exit label after the <c>catch</c>, which disposes and then
/// jumps on, through the exit labels of the outer <c>using</c>s it leaves too; a <c>return</c> with a value keeps it in
/// <c>__gsharp_using_return</c> meanwhile, as C# evaluates it before the <c>finally</c>. Several declarators are nested
/// <c>using</c>s, so that a failure of a later initializer disposes the earlier resources, as C# does.
/// </para>
/// </remarks>
internal static class UsingCpuEmitter
{
    /// <summary>
    /// The GSH0003 text for a value returned out of a using whose function has no return type to keep it in.
    /// </summary>
    public const string ReturnTypeUnknown =
        "return of a value out of a using in a function without a known return type";

    /// <summary>Writes a <c>using</c> statement.</summary>
    public static void TranspileUsingStatement(UsingStatementSyntax node, ref TranspileContext context)
    {
        var plan = UsingExitPlan.Create(node, node.Statement, node.Statement, context.Query.SemanticModel,
            context.Emitter.MethodSymbol);
        var body = new UsingBody(node.Statement, default, 0);
        using (context.Emitter.Writer.Scope())
        {
            if (node.Declaration is { } declaration)
            {
                WriteDeclarationUsing(node, declaration, plan, body, ref context);
            }
            else if (node.Expression is { } expression)
            {
                WriteExpressionUsing(expression, plan, body, ref context);
            }
            else
            {
                body.Write(ref context);
            }
        }
    }

    /// <summary>
    /// The <c>using</c> declaration that <paramref name="statement"/> is, also under labels, that the CPU form writes
    /// (an <c>await using</c> is reported by <see cref="UsingTranspiler"/>).
    /// </summary>
    public static bool TryGetUsingDeclaration(StatementSyntax statement,
        [NotNullWhen(true)] out LocalDeclarationStatementSyntax? declaration)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        declaration = statement as LocalDeclarationStatementSyntax;
        return declaration is
        {
            UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword,
            AwaitKeyword.RawKind: (int)SyntaxKind.None
        };
    }

    /// <summary>
    /// Writes the <c>using</c> declaration <paramref name="node"/>, which is <c>statements[index]</c> or its labeled
    /// statement, with the statements after it, which are its body.
    /// </summary>
    public static void TranspileUsingDeclaration(LocalDeclarationStatementSyntax node,
        SyntaxList<StatementSyntax> statements, int index, ref TranspileContext context)
    {
        var first = index + 1 < statements.Count ? statements[index + 1] : null;
        var plan = UsingExitPlan.Create(node, first, statements[statements.Count - 1], context.Query.SemanticModel,
            context.Emitter.MethodSymbol);
        var body = new UsingBody(null, statements, index + 1);
        using (context.Emitter.Writer.Scope())
        {
            WriteDeclarationUsing(node, node.Declaration, plan, body, ref context);
        }
    }

    /// <summary>
    /// When <paramref name="jump"/> leaves the body of the innermost <c>using</c> being written, writes the jump to its
    /// exit label (keeping the value of a <c>return</c> first) and returns true.
    /// </summary>
    public static bool TryJumpToExit(StatementSyntax jump, ref TranspileContext context)
    {
        var scopes = context.Emitter.UsingScopes;
        if (scopes.Count == 0)
        {
            return false;
        }

        var scope = scopes[scopes.Count - 1];
        var exit = scope.Plan.FindExit(jump);
        if (exit < 0)
        {
            return false;
        }

        var writer = context.Emitter.Writer;
        if (jump is ReturnStatementSyntax { Expression: { } value } && context.Emitter.UsingReturnValue == null)
        {
            // __gsharp_using_return = <value>; or = &(<value>); for a function that returns by reference
            StatementPrelude.Emit(value, ref context);
            writer.Append($"{GSharpInternalNames.Using.ReturnValue} = ");
            if (scope.Plan.ReturnsByRef)
            {
                writer.Append('&');
                using (writer.Parentheses())
                {
                    context.Visitor.Visit(value);
                }
            }
            else
            {
                context.Visitor.Visit(value);
            }

            writer.AppendLine(";");
        }

        writer.Append($"goto {GSharpInternalNames.Using.ExitPrefix}{scope.Number}_{exit};");
        return true;
    }

    // <type> <name> = <init>; protected, then the next declarator, the last one around the body
    private static void WriteDeclarationUsing(SyntaxNode node, VariableDeclarationSyntax declaration,
        UsingExitPlan plan, UsingBody body, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(declaration.Type);
        if (!UsingTranspiler.TryUseDispose(type, declaration.Type, ref context, out _))
        {
            StatementPrelude.Emit(node is LocalDeclarationStatementSyntax ? node : declaration, ref context);
            context.Visitor.Visit(declaration);
            context.Emitter.Writer.AppendLine(";");
            body.Write(ref context);
            return;
        }

        WriteReturnValue(node, plan, ref context);
        WriteLevel(new UsingLevel(declaration, 0, body), plan, ref context);
    }

    // T __gsharp_using_resource_<n> = <expression>; protected
    // [WHY] C# disposes a copy of the expression's value, which a later assignment to its source does not change.
    private static void WriteExpressionUsing(ExpressionSyntax expression, UsingExitPlan plan, UsingBody body,
        ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(expression);
        if (!UsingTranspiler.TryUseDispose(type, expression, ref context, out var resourceType))
        {
            context.Visitor.Visit(expression);
            context.Emitter.Writer.AppendLine(";");
            body.Write(ref context);
            return;
        }

        WriteReturnValue(expression, plan, ref context);
        var resource = GSharpInternalNames.Using.ResourcePrefix + context.Emitter.UsingCounter++;
        context.Emitter.Dialect.AppendType(ref context, resourceType);
        context.Emitter.Writer.Append($" {resource} = ");
        context.Visitor.Visit(expression);
        context.Emitter.Writer.AppendLine(";");
        WriteProtected(resource, new UsingLevel(null, 0, body), plan, ref context);
    }

    // The declarator of the level and the levels inside it, or the body after the last declarator
    private static void WriteLevel(UsingLevel level, UsingExitPlan plan, ref TranspileContext context)
    {
        if (level.Declaration is not { } declaration || level.Index >= declaration.Variables.Count)
        {
            level.Body.Write(ref context);
            return;
        }

        var variable = declaration.Variables[level.Index];
        StatementPrelude.Emit(variable, ref context);
        VariableDeclarationTranspilerRule.WriteUsingResource(declaration, variable, ref context);
        context.Emitter.Writer.AppendLine(";");
        var resource = CppNameResolver
            .Resolve(variable, context.Query.SemanticModel, context.Emitter.Dialect)
            .ToString();
        WriteProtected(resource, new UsingLevel(declaration, level.Index + 1, level.Body), plan, ref context);
    }

    // try { <inner> } catch (...) { <dispose on a failure> } <exit labels> <done label> <resource>.__gsharp_dispose();
    private static void WriteProtected(string resource, UsingLevel inner, UsingExitPlan plan,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var number = context.Emitter.UsingCounter++;
        var hasExits = plan.Exits.Length > 0;
        writer.AppendLine("try");
        context.Emitter.UsingScopes.Add(new UsingCpuScope(number, plan));
        try
        {
            using (writer.Scope())
            {
                WriteLevel(inner, plan, ref context);
                writer.AppendLineIf(hasExits, $"goto {GSharpInternalNames.Using.DonePrefix}{number};");
            }
        }
        finally
        {
            context.Emitter.UsingScopes.RemoveAt(context.Emitter.UsingScopes.Count - 1);
        }

        WriteCatch(resource, ref context);
        for (var i = 0; i < plan.Exits.Length; i++)
        {
            writer.AppendLine($"{GSharpInternalNames.Using.ExitPrefix}{number}_{i}:");
            WriteDispose(resource, ref context);
            WriteExitJump(plan.Exits[i], plan.ReturnsByRef, ref context);
        }

        writer.AppendLineIf(hasExits, $"{GSharpInternalNames.Using.DonePrefix}{number}:");
        WriteDispose(resource, ref context);
    }

    // catch (...) { int __gsharp_using_outer = __gsharp_using_enter(); try { <resource>.__gsharp_dispose(); }
    // catch (...) { __gsharp_using_dispose_failed(); } __gsharp_using_leave(__gsharp_using_outer); throw; }
    // [WHY] The helpers take no address of the resource: GCC would keep it in memory on the normal path as well.
    private static void WriteCatch(string resource, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.AppendLine("catch (...)");
        using (writer.Scope())
        {
            writer.AppendLine($"int {GSharpInternalNames.Using.Outer} = {GSharpInternalNames.Using.Enter}();");
            writer.AppendLine("try");
            using (writer.Scope())
            {
                WriteDispose(resource, ref context);
            }

            writer.AppendLine("catch (...)");
            using (writer.Scope())
            {
                writer.AppendLine($"{GSharpInternalNames.Using.DisposeFailed}();");
            }

            writer.AppendLine($"{GSharpInternalNames.Using.Leave}({GSharpInternalNames.Using.Outer});");
            writer.AppendLine("throw;");
        }
    }

    // <resource>.__gsharp_dispose();
    private static void WriteDispose(string resource, ref TranspileContext context) =>
        context.Emitter.Writer.AppendLine($"{resource}.{GSharpInternalNames.Using.Dispose}();");

    // The jump of the exit, written again after the Dispose: it goes on through the exit label of the next using it
    // leaves, or jumps as it does without a using; a return reads the value it kept
    private static void WriteExitJump(UsingExit exit, bool returnsByRef, ref TranspileContext context)
    {
        var saved = context.Emitter.UsingReturnValue;
        if (exit.Jump is ReturnStatementSyntax { Expression: not null })
        {
            context.Emitter.UsingReturnValue = returnsByRef
                ? "*" + GSharpInternalNames.Using.ReturnValue
                : GSharpInternalNames.Using.ReturnValue;
        }

        try
        {
            context.Visitor.Visit(exit.Jump);
        }
        finally
        {
            context.Emitter.UsingReturnValue = saved;
        }

        context.Emitter.Writer.AppendLine();
    }

    // <return type> __gsharp_using_return{}; (a pointer for a function that returns by reference) before the outermost
    // using of the function that a return with a value leaves (UsingExitPlan.DeclaresReturnValue)
    private static void WriteReturnValue(SyntaxNode node, UsingExitPlan plan, ref TranspileContext context)
    {
        if (!plan.ReturnsValue)
        {
            return;
        }

        if (plan.ReturnType is not { } returnType)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ReturnTypeUnknown);
            return;
        }

        if (!plan.DeclaresReturnValue)
        {
            return;
        }

        context.Emitter.Dialect.AppendType(ref context, returnType);
        context.Emitter.Writer.AppendIf(plan.ReturnsByRef, "*");
        context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Using.ReturnValue}{{}};");
    }

    /// <summary>
    /// The body of a <c>using</c>: its statement, or the statements after a <c>using</c> declaration.
    /// </summary>
    private readonly struct UsingBody(StatementSyntax? statement, SyntaxList<StatementSyntax> statements, int start)
    {
        public void Write(ref TranspileContext context)
        {
            switch (statement)
            {
                case BlockSyntax block:
                    context.Visitor.VisitBlock(block);
                    break;
                case not null:
                    context.Visitor.Visit(statement);
                    context.Emitter.Writer.AppendLine();
                    break;
                default:
                    context.Visitor.VisitStatements(statements, start);
                    break;
            }
        }
    }

    /// <summary>
    /// What a protected resource encloses: the declarator <paramref name="Index"/> of <paramref name="Declaration"/>
    /// and the levels after it, or the body.
    /// </summary>
    private readonly record struct UsingLevel(VariableDeclarationSyntax? Declaration, int Index, UsingBody Body);
}
