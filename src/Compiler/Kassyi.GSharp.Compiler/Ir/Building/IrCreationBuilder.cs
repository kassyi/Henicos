using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>Builds the creation of a struct value and of an array.</summary>
internal static class IrCreationBuilder
{
    /// <summary>
    /// Builds <c>new S(args) { F = v }</c> of a struct. A class, a nested member initializer or a collection
    /// initializer
    /// keeps the whole creation opaque, since <see cref="IrStructCreation.Initializers"/> holds assignments only.
    /// </summary>
    /// <remarks>
    /// The nested member initializer (<c>new S { I = { B = 5 } }</c>) is to be built as nodes when struct creation
    /// moves
    /// to the IR on every dialect; until then the coverage test counts it.
    /// </remarks>
    public static IrExpression BuildStruct(IObjectCreationOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        if (!type.IsValueType)
        {
            return IrExpressionBuilder.Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var initializers = ImmutableArray<IrAssignment>.Empty;
        if (operation.Initializer is { } initializer)
        {
            var builder = ImmutableArray.CreateBuilder<IrAssignment>(initializer.Initializers.Length);
            foreach (var member in initializer.Initializers)
            {
                if (member is not ISimpleAssignmentOperation { Type: { } memberType } assignment ||
                    IrExpressionBuilder.Build(assignment, IrCategories.ForRead(memberType), context) is not
                        IrAssignment built)
                {
                    return IrExpressionBuilder.Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
                }

                builder.Add(built);
            }

            initializers = builder.MoveToImmutable();
        }

        var arguments = ImmutableArray<IrArgument>.Empty;
        if (operation.Constructor is { } constructor &&
            !IrArgumentBuilder.TryBuild(operation.Arguments, constructor.Parameters, context, out arguments,
                out var failure))
        {
            return IrExpressionBuilder.Opaque(operation, failure, category, context);
        }

        return new IrStructCreation(operation.Constructor, arguments, initializers, type, category, source);
    }

    /// <summary>Builds <c>new T[n]</c> or <c>new T[] { ... }</c> of one dimension.</summary>
    public static IrExpression BuildArray(IArrayCreationOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        if (operation.DimensionSizes is not [var length] || type is not IArrayTypeSymbol arrayType)
        {
            return IrExpressionBuilder.Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var elements = ImmutableArray<IrExpression>.Empty;
        if (operation.Initializer is { } initializer)
        {
            var builder = ImmutableArray.CreateBuilder<IrExpression>(initializer.ElementValues.Length);
            foreach (var element in initializer.ElementValues)
            {
                builder.Add(IrExpressionBuilder.BuildRead(element, context));
            }

            elements = builder.MoveToImmutable();
        }

        return new IrArrayCreation(arrayType.ElementType, IrExpressionBuilder.BuildRead(length, context), elements,
            type, category, source);
    }
}
