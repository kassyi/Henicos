namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the declarations of ref locals: a pointer (<c>T* a = &amp;x;</c>), as a <c>ref</c> / <c>out</c> parameter is
/// (<see cref="RefSyntaxHelper"/>).
/// </summary>
/// <remarks>
/// [WHY] The uses of a ref local are already written as <c>(*a)</c> (<c>IdentifierNameTranspiler</c>); a C++ reference
/// cannot be rebound (<c>a = ref y;</c>), so the local is a pointer. The initializer <c>ref x</c> is the address of
/// <c>x</c> (a <c>ref</c> / <c>out</c> parameter or another ref local, already a pointer, is not wrapped twice); a
/// conditional <c>ref (c ? ref a : ref b)</c> is the conditional of the two addresses. A ref local that a <c>goto</c>
/// may jump past is declared and assigned apart, as the other locals are (<see cref="GotoJumpAnalysis"/>). Roslyn does
/// the same in IL: a ref local is a managed pointer local, and the iteration variable of <c>foreach (ref ...)</c> is
/// declared in the body of each iteration.
/// </remarks>
internal static class RefLocals
{
    /// <summary>Writes the pointer that the ref expression <paramref name="node"/> stands for.</summary>
    public static void AppendReferenced(RefExpressionSyntax node, ref TranspileContext context)
    {
        // ref (c ? ref a : ref b): the branches write their own addresses
        if (Unparenthesized(node.Expression) is ConditionalExpressionSyntax conditional &&
            (conditional.WhenTrue is RefExpressionSyntax || conditional.WhenFalse is RefExpressionSyntax))
        {
            context.Visitor.Visit(node.Expression);
            return;
        }

        RefSyntaxHelper.TranspileAsByRefArgument(ref context, node.Expression);
    }

    /// <summary>
    /// Writes <c>&lt;type&gt;* &lt;name&gt; = &lt;pointer&gt;</c> for the declarator of a ref local, or
    /// <see langword="false"/> when <paramref name="declarator"/> is not one.
    /// </summary>
    public static bool TryWriteDeclaration(VariableDeclaratorSyntax declarator, ref bool hasEmittedAny,
        ref TranspileContext context)
    {
        if (declarator.Parent is not VariableDeclarationSyntax { Type: RefTypeSyntax } declaration ||
            context.Query.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator) is not { } local)
        {
            return false;
        }

        if (hasEmittedAny)
        {
            context.Emitter.Writer.AppendLine(";");
        }

        hasEmittedAny = true;
        WriteType(local, ref context);
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        if (declarator.Initializer == null)
        {
            return true;
        }

        // <type>* <name>;
        // <name> = <pointer>
        if (declaration.Parent is StatementSyntax statement && GotoJumpAnalysis.MayBeJumpedOver(statement))
        {
            context.Emitter.Writer.AppendLine(";");
            CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        }

        DeclarationTranspilerHelper.WriteDeclaratorInitializer(declarator, ref context);
        return true;
    }

    /// <summary>
    /// Writes <c>&lt;name&gt; = &lt;pointer&gt;</c> for <c>a = ref y;</c>, or <see langword="false"/> when
    /// <paramref name="node"/> is not the rebinding of a ref local.
    /// </summary>
    public static bool TryWriteRebinding(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            node.Right is not RefExpressionSyntax ||
            node.Left is not IdentifierNameSyntax left ||
            context.Query.SemanticModel.GetSymbol(left) is not ILocalSymbol { IsRef: true } local)
        {
            return false;
        }

        CppNameResolver.AppendSymbolName(local, ref context);
        context.Emitter.Writer.Append('=');
        context.Visitor.Visit(node.Right);
        return true;
    }

    /// <summary>
    /// Writes <c>{ &lt;type&gt;* e = &amp;__gsharp_foreach_e_&lt;n&gt;; &lt;body&gt; }</c>, the body of a
    /// <c>foreach (ref ...)</c> whose loop variable is the pointer to the element.
    /// </summary>
    public static void WriteForEachElementBody(ForEachStatementSyntax node, int rawElement,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            if (context.Query.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(node) is { } local)
            {
                WriteType(local, ref context);
            }

            CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
            context.Emitter.Writer.AppendLine($" = &{GSharpInternalNames.Lowering.ForEachElementPrefix}{rawElement};");
            if (node.Statement is BlockSyntax block)
            {
                context.Visitor.VisitBlock(block);
            }
            else
            {
                context.Visitor.Visit(node.Statement);
                context.Emitter.Writer.AppendLine();
            }
        }
    }

    // [const ]<type>* , the type of the pointer that the ref local holds
    private static void WriteType(ILocalSymbol local, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendIf(local.RefKind == RefKind.RefReadOnly, "const ");
        context.Emitter.Dialect.AppendType(ref context, local.Type);
        context.Emitter.Writer.Append("* ");
    }

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
