using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// Builds the statements that have nodes (blocks, local declarations, expression statements, <see langword="return"/>,
/// <see langword="if"/>); every other statement is an <see cref="IrOpaqueStatement"/> leaf.
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

        var locals = ImmutableArray.CreateBuilder<IrVariable>(block.Locals.Length);
        foreach (var local in block.Locals)
        {
            locals.Add(context.Variables.GetLocal(local));
        }

        return new IrBlock(statements.ToImmutable(), locals.MoveToImmutable(), context.SourceOf(block));
    }

    /// <summary>Builds one statement; a declaration of several locals becomes a block of declarations.</summary>
    public static IrStatement Build(IOperation statement, IrBuildContext context)
    {
        if (statement is IBlockOperation block)
        {
            return BuildBlock(block, context);
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
        switch (statement)
        {
            case IBlockOperation block:
                statements.Add(BuildBlock(block, context));
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
