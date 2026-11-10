using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// Builds the arguments of a call, an indexer or a constructor in parameter order, with the default values C# supplies
/// and the position of each argument in the C# order of evaluation.
/// </summary>
internal static class IrArgumentBuilder
{
    /// <summary>
    /// Builds <paramref name="arguments"/> (in Roslyn's order, which is the order of evaluation) into one argument per
    /// parameter, or the reason the whole call stays opaque.
    /// </summary>
    public static bool TryBuild(ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<IParameterSymbol> parameters, IrBuildContext context, out ImmutableArray<IrArgument> built,
        out IrOpaqueReason failure)
    {
        built = default;
        failure = IrOpaqueReason.Invalid;
        if (arguments.Length != parameters.Length)
        {
            return false;
        }

        var slots = new IrArgument?[parameters.Length];
        return TryFillSlots(arguments, slots, context, ref failure) && TryCollect(slots, out built);
    }

    // Puts each argument in the slot of its parameter; fails on an argument without a parameter or a parameter twice
    private static bool TryFillSlots(ImmutableArray<IArgumentOperation> arguments, IrArgument?[] slots,
        IrBuildContext context, ref IrOpaqueReason failure)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.Parameter is not { } parameter)
            {
                failure = IrOpaqueReason.UnsupportedShape;
                return false;
            }

            var ordinal = parameter.Ordinal;
            if (ordinal >= slots.Length || slots[ordinal] is not null)
            {
                return false;
            }

            var value = IrExpressionBuilder.Build(argument.Value, IrCategories.ForArgument(parameter, argument.Value),
                context);
            slots[ordinal] = new IrArgument(parameter, value, KindOf(argument.ArgumentKind), index);
        }

        return true;
    }

    // The arguments in parameter order; fails when a parameter got none
    private static bool TryCollect(IrArgument?[] slots, out ImmutableArray<IrArgument> built)
    {
        built = default;
        var builder = ImmutableArray.CreateBuilder<IrArgument>(slots.Length);
        foreach (var slot in slots)
        {
            if (slot is not { } filled)
            {
                return false;
            }

            builder.Add(filled);
        }

        built = builder.MoveToImmutable();
        return true;
    }

    private static IrArgumentKind KindOf(ArgumentKind kind) => kind switch
    {
        ArgumentKind.DefaultValue => IrArgumentKind.DefaultValue,
        ArgumentKind.ParamArray => IrArgumentKind.ParamArray,
        _ => IrArgumentKind.Explicit
    };
}
