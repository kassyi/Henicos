using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// Builds the statements that have nodes (blocks, local declarations, expression statements, <see langword="return"/>,
/// <see langword="if"/>, <see langword="while"/>); every other statement is an <see cref="IrOpaqueStatement"/> leaf.
/// </summary>
internal static class IrStatementBuilder
{
    /// <summary>Builds a block (a method body, a nested block) with the locals of its scope.</summary>
    public static IrBlock BuildBlock(IBlockOperation block, IrBuildContext context)
    {
        var statements = ImmutableArray.CreateBuilder<IrStatement>(block.Operations.Length);
        foreach (var statement in block.Operations)
        {
            AddTo(statements, statement, context);
        }

        var locals = LocalsOf(block.Locals, context);
        return new IrBlock(statements.ToImmutable(), locals, context.SourceOf(block));
    }

    /// <summary>Builds one statement; a declaration of several locals becomes a block of declarations.</summary>
    public static IrStatement Build(IOperation statement, IrBuildContext context)
    {
        if (statement is IBlockOperation { Syntax.Parent: not CheckedStatementSyntax } block)
        {
            var builtBlock = BuildBlock(block, context);
            var blockComments = IrSpelling.CommentsBefore(statement.Syntax, statement.IsImplicit);
            return blockComments.IsEmpty ? builtBlock : builtBlock.WithCommentsBefore(blockComments);
        }

        var built = ImmutableArray.CreateBuilder<IrStatement>();
        AddTo(built, statement, context);
        return built is [var single]
            ? single
            : new IrBlock(built.ToImmutable(), ImmutableArray<IrVariable>.Empty,
                context.SourceOf(statement).AsSynthesized());
    }

    // Adds the statements of one operation: several for a declaration of several locals
    private static void AddTo(ImmutableArray<IrStatement>.Builder statements, IOperation statement,
        IrBuildContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var first = statements.Count;
        switch (statement)
        {
            case IBlockOperation { Syntax.Parent: CheckedStatementSyntax checkedStatement } checkedBlock:
                // [WHY] The analyzer reports checked and unchecked statements (GSH0003), so they stay to the
                // syntax rules.
                // Roslyn binds the block of a checked statement, so the statement is the block's parent
                statements.Add(new IrOpaqueStatement(checkedStatement, checkedBlock, IrOpaqueReason.UnsupportedShape,
                    IrOpaqueSplit.Unclassified, context.SourceOf(checkedStatement, synthesized: false)));
                break;
            case IBlockOperation block:
                statements.Add(BuildBlock(block, context));
                break;
            case IWhileLoopOperation { ConditionIsTop: true, ConditionIsUntil: false, Condition: { } condition } loop:
                statements.Add(new IrWhile(IrExpressionBuilder.BuildRead(condition, context), Build(loop.Body, context),
                    LocalsOf(loop.Locals, context), context.SourceOf(loop)));
                break;
            case IVariableDeclarationGroupOperation group:
                AddDeclarations(statements, group, context);
                break;
            case IExpressionStatementOperation expression:
                statements.Add(new IrExpressionStatement(
                    IrExpressionBuilder.BuildRead(expression.Operation, context), context.SourceOf(expression)));
                break;
            case IReturnOperation { Kind: OperationKind.Return } @return:
                statements.Add(new IrReturn(BuildReturnValue(@return, context), context.SourceOf(@return)));
                break;
            case IConditionalOperation { WhenTrue: { } then } conditional when conditional.Type is null:
                statements.Add(new IrIf(IrExpressionBuilder.BuildRead(conditional.Condition, context),
                    Build(then, context), conditional.WhenFalse is { } @else ? Build(@else, context) : null,
                    context.SourceOf(conditional)));
                break;
            default:
                statements.Add(new IrOpaqueStatement(statement.Syntax, statement, IrOpaqueReason.UnsupportedOperation,
                    IrOpaqueSplit.Unclassified, context.SourceOf(statement)));
                break;
        }

        var comments = IrSpelling.CommentsBefore(CommentSyntaxOf(statement), statement.IsImplicit);
        if (statements.Count > first && !comments.IsEmpty)
        {
            statements[first] = statements[first].WithCommentsBefore(comments);
        }
    }

    private static SyntaxNode CommentSyntaxOf(IOperation statement) =>
        statement is IBlockOperation { Syntax.Parent: CheckedStatementSyntax checkedStatement }
            ? checkedStatement
            : statement.Syntax;

    private static ImmutableArray<IrVariable> LocalsOf(ImmutableArray<ILocalSymbol> symbols, IrBuildContext context)
    {
        if (symbols.IsEmpty)
        {
            return ImmutableArray<IrVariable>.Empty;
        }

        var locals = ImmutableArray.CreateBuilder<IrVariable>(symbols.Length);
        foreach (var local in symbols)
        {
            locals.Add(context.Variables.GetLocal(local));
        }

        return locals.MoveToImmutable();
    }

    // int a = 1, b = a; is one declaration per local, in the order written
    private static void AddDeclarations(ImmutableArray<IrStatement>.Builder statements,
        IVariableDeclarationGroupOperation group, IrBuildContext context)
    {
        foreach (var declaration in group.Declarations)
        {
            foreach (var declarator in declaration.Declarators)
            {
                var local = declarator.Symbol;
                var initializer = (declarator.Initializer ?? declaration.Initializer)?.Value;
                var category = local.RefKind == RefKind.None
                    ? IrCategories.ForRead(local.Type)
                    : IrCategories.ForReference(local.RefKind == RefKind.RefReadOnly ? RefKind.In : RefKind.Ref);
                var value = initializer is null ? null : IrExpressionBuilder.Build(initializer, category, context);
                statements.Add(new IrLocalDeclaration(context.Variables.GetLocal(local), value,
                    context.SourceOf(declarator)));
            }
        }
    }

    private static IrExpression? BuildReturnValue(IReturnOperation operation, IrBuildContext context)
    {
        if (operation.ReturnedValue is not { } value)
        {
            return null;
        }

        var refKind = context.Method.RefKind;
        return refKind == RefKind.None
            ? IrExpressionBuilder.BuildRead(value, context)
            : IrExpressionBuilder.Build(value,
                IrCategories.ForReference(refKind == RefKind.RefReadOnly ? RefKind.In : RefKind.Ref), context);
    }
}
