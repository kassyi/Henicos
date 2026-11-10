using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Writes the expressions of an <see cref="IrExpressionPrinter"/> that are neither references nor operators: struct
/// and array creations, placeholders (<c>&lt;init&gt;</c>, <c>default</c>, <c>_</c>), addresses, sequences and opaque
/// leaves.
/// </summary>
internal static class IrCreationPrinter
{
    /// <summary>Writes a creation, a placeholder, a sequence or an opaque leaf.</summary>
    public static void Write(IrExpressionPrinter printer, IrPrinter owner, SourceWriter writer,
        IrExpression expression)
    {
        switch (expression)
        {
            case IrStructCreation creation:
                WriteStructCreation(printer, writer, creation);
                break;
            case IrInitializedObject:
                writer.Append("<init>");
                break;
            case IrArrayCreation array:
                WriteArrayCreation(printer, writer, array);
                break;
            case IrAddressOf address:
                WriteAddressOf(printer, writer, address);
                break;
            case IrDefaultValue:
                writer.Append($"default({IteratorTypeResolver.FormatType(expression.Type)})");
                break;
            case IrDiscard:
                writer.Append("_");
                break;
            case IrSequence sequence:
                WriteSequence(printer, owner, writer, sequence);
                break;
            case IrOpaqueExpression opaque:
                WriteOpaque(writer, opaque);
                break;
            default:
                throw new InvalidOperationException(
                    "The IR printer has no form for " + expression.GetType().Name + ".");
        }
    }

    private static void WriteAddressOf(IrExpressionPrinter printer, SourceWriter writer, IrAddressOf address)
    {
        writer.Append("&");
        printer.WriteOperand(address.Operand);
    }

    private static void WriteStructCreation(IrExpressionPrinter printer, SourceWriter writer,
        IrStructCreation creation)
    {
        writer.Append($"new {IteratorTypeResolver.FormatType(creation.Type)}");
        using (writer.Parentheses())
        {
            printer.WriteArguments(creation.Arguments);
        }

        WriteList(printer, writer, creation.Initializers);
    }

    private static void WriteArrayCreation(IrExpressionPrinter printer, SourceWriter writer,
        IrArrayCreation array)
    {
        writer.Append($"new {IteratorTypeResolver.FormatType(array.ElementType)}");
        using (writer.Brackets())
        {
            printer.WriteExpression(array.Length);
        }

        WriteList(printer, writer, array.Elements);
    }

    private static void WriteSequence(IrExpressionPrinter printer, IrPrinter owner, SourceWriter writer,
        IrSequence sequence)
    {
        writer.Append("seq");
        using (writer.Parentheses())
        {
            for (var index = 0; index < sequence.Locals.Length; index++)
            {
                writer.AppendIf(index > 0, ", ");
                owner.WriteVariable(sequence.Locals[index]);
            }

            writer.Append("; ");
            foreach (var sideEffect in sequence.SideEffects)
            {
                writer.Append(owner.PrintInline(sideEffect));
                writer.Append("; ");
            }

            printer.WriteExpression(sequence.Value);
        }
    }

    private static void WriteOpaque(SourceWriter writer, IrOpaqueExpression opaque)
    {
        writer.Append($"opaque:{IrPrintText.Reason(opaque.Reason)}");
        using (writer.Parentheses())
        {
            writer.Append(IrPrintText.FirstLine(opaque.Syntax));
        }
    }

    private static void WriteList<T>(IrExpressionPrinter printer, SourceWriter writer, ImmutableArray<T> items)
        where T : IrExpression
    {
        if (items.IsEmpty)
        {
            return;
        }

        writer.Append(" ");
        using (writer.Braces())
        {
            for (var index = 0; index < items.Length; index++)
            {
                writer.AppendIf(index > 0, ", ");
                printer.WriteExpression(items[index]);
            }
        }
    }
}
