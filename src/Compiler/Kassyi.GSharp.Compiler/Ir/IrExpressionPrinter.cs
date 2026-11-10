using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Writes the expressions of an <see cref="IrPrinter"/> in the form it describes.</summary>
/// <param name="owner">The printer whose variables and statements the expressions refer to.</param>
/// <param name="writer">The output.</param>
/// <param name="options">What is written besides the nodes.</param>
internal sealed class IrExpressionPrinter(IrPrinter owner, SourceWriter writer, IrPrintOptions options)
{
    private readonly IrPrinter _owner = owner;
    private readonly SourceWriter _writer = writer;
    private readonly IrPrintOptions _options = options;

    /// <summary>Writes an expression with its category and, as asked, its span.</summary>
    public void WriteExpression(IrExpression expression)
    {
        _writer.Append(IrPrintText.Category(expression.Category));
        WriteForm(expression);
        if ((_options & IrPrintOptions.Sources) != 0)
        {
            var source = expression.Source;
            _writer.Append($"[{(source.Synthesized ? "~" : "")}{source.Start}..{source.Start + source.Length}]");
        }
    }

    // Constants, variables, members, elements and calls
    private void WriteForm(IrExpression expression)
    {
        switch (expression)
        {
            case IrConstant constant:
                WriteConstant(constant);
                break;
            case IrVariableReference reference:
                WriteReference(reference);
                break;
            case IrFieldReference field:
                WriteMember(field.Receiver, field.Field.ContainingType, field.Field.Name);
                break;
            case IrElementReference element:
                WriteElement(element);
                break;
            case IrPropertyReference property:
                WriteProperty(property);
                break;
            case IrCall call:
                WriteCall(call);
                break;
            default:
                WriteOperatorForm(expression);
                break;
        }
    }

    // Operators, conditionals and conversions
    private void WriteOperatorForm(IrExpression expression)
    {
        switch (expression)
        {
            case IrUnary unary:
                WriteUnary(unary);
                break;
            case IrBinary binary:
                WriteInfix(binary.Left, IrPrintText.Binary(binary.Operator.Kind), binary.Right,
                    binary.Operator.IsChecked, binary.Operator.Method);
                break;
            case IrLogical logical:
                WriteInfix(logical.Left, logical.IsAnd ? "&&" : "||", logical.Right);
                break;
            case IrConditional conditional:
                WriteConditional(conditional);
                break;
            case IrNullTest test:
                WriteNullTest(test);
                break;
            case IrCoalesce coalesce:
                WriteInfix(coalesce.Value, "??", coalesce.WhenNull);
                break;
            case IrConversion conversion:
                WriteConversion(conversion);
                break;
            default:
                WriteAssignmentForm(expression);
                break;
        }
    }

    // Assignments and increments
    private void WriteAssignmentForm(IrExpression expression)
    {
        switch (expression)
        {
            case IrAssignment assignment:
                WriteInfix(assignment.Target, "=", assignment.Value);
                break;
            case IrCompoundAssignment compound:
                WriteInfix(compound.Target, IrPrintText.Binary(compound.Operator.Kind) + "=", compound.Value,
                    compound.Operator.IsChecked, compound.Operator.Method);
                break;
            case IrIncrement increment:
                WriteIncrement(increment);
                break;
            default:
                WriteOtherForm(expression);
                break;
        }
    }

    // Creations, placeholders, sequences and opaque leaves
    private void WriteOtherForm(IrExpression expression)
    {
        switch (expression)
        {
            case IrStructCreation creation:
                WriteStructCreation(creation);
                break;
            case IrInitializedObject:
                _writer.Append("<init>");
                break;
            case IrArrayCreation array:
                WriteArrayCreation(array);
                break;
            case IrAddressOf address:
                WriteAddressOf(address);
                break;
            case IrDefaultValue:
                _writer.Append($"default({IteratorTypeResolver.FormatType(expression.Type)})");
                break;
            case IrDiscard:
                _writer.Append("_");
                break;
            case IrSequence sequence:
                WriteSequence(sequence);
                break;
            case IrOpaqueExpression opaque:
                WriteOpaque(opaque);
                break;
            default:
                throw new InvalidOperationException(
                    "The IR printer has no form for " + expression.GetType().Name + ".");
        }
    }

    private void WriteAddressOf(IrAddressOf address)
    {
        _writer.Append("&");
        WriteOperand(address.Operand);
    }

    private void WriteConstant(IrConstant constant)
    {
        _writer.AppendIf(constant.Origin is not null, constant.Origin?.Name + "=");
        if (constant.Type.TypeKind == TypeKind.Enum)
        {
            _writer.Append($"({IteratorTypeResolver.FormatType(constant.Type)})");
        }

        _writer.Append(IrPrintText.Constant(constant.Value));
    }

    private void WriteReference(IrVariableReference reference)
    {
        _writer.AppendIf(reference.IsDeclaration, "var ");
        _owner.WriteVariable(reference.Variable);
    }

    private void WriteMember(IrExpression? receiver, ITypeSymbol containingType, string name)
    {
        WriteReceiver(receiver, containingType);
        _writer.Append($".{name}");
    }

    private void WriteElement(IrElementReference element)
    {
        WriteOperand(element.Receiver);
        using (_writer.Brackets())
        {
            WriteExpression(element.Index);
        }
    }

    private void WriteProperty(IrPropertyReference property)
    {
        if (!property.Property.IsIndexer)
        {
            WriteMember(property.Receiver, property.Property.ContainingType, property.Property.Name);
            return;
        }

        WriteReceiver(property.Receiver, property.Property.ContainingType);
        using (_writer.Brackets())
        {
            WriteArguments(property.Arguments);
        }
    }

    private void WriteCall(IrCall call)
    {
        WriteMember(call.Receiver, call.Method.ContainingType, call.Method.Name);
        using (_writer.Parentheses())
        {
            WriteArguments(call.Arguments);
        }

        WriteEvaluationOrder(call.Arguments);
    }

    private void WriteUnary(IrUnary unary)
    {
        using (_writer.Parentheses())
        {
            _writer.AppendIf(unary.Operator.IsChecked, "checked ");
            _writer.Append(IrPrintText.Unary(unary.Operator.Kind));
            WriteOperatorMethod(unary.Operator.Method);
            WriteExpression(unary.Operand);
        }
    }

    private void WriteInfix(IrExpression left, string operatorText, IrExpression right, bool isChecked = false,
        IMethodSymbol? operatorMethod = null)
    {
        using (_writer.Parentheses())
        {
            _writer.AppendIf(isChecked, "checked ");
            WriteExpression(left);
            _writer.Append($" {operatorText}");
            WriteOperatorMethod(operatorMethod);
            _writer.Append(" ");
            WriteExpression(right);
        }
    }

    // A user-defined operator follows the operator text: (a +@op_Addition b)
    private void WriteOperatorMethod(IMethodSymbol? operatorMethod) =>
        _writer.AppendIf(operatorMethod is not null, "@" + operatorMethod?.Name);

    private void WriteConditional(IrConditional conditional)
    {
        using (_writer.Parentheses())
        {
            WriteExpression(conditional.Condition);
            _writer.Append(" ? ");
            WriteExpression(conditional.WhenTrue);
            _writer.Append(" : ");
            WriteExpression(conditional.WhenFalse);
        }
    }

    private void WriteNullTest(IrNullTest test)
    {
        using (_writer.Parentheses())
        {
            WriteExpression(test.Operand);
            _writer.Append(test.IsNegated ? " is not null" : " is null");
        }
    }

    private void WriteConversion(IrConversion conversion)
    {
        var type = IteratorTypeResolver.FormatType(conversion.Type);
        var kind = IrPrintText.ConversionKind(conversion.Conversion.Kind);
        if (conversion.IsImplicit)
        {
            _writer.Append($"implicit{kind}<{type}>");
            using (_writer.Parentheses())
            {
                WriteExpression(conversion.Operand);
            }

            return;
        }

        _writer.AppendIf(conversion.Conversion.IsChecked, "checked");
        using (_writer.Parentheses())
        {
            _writer.Append($"{type}{kind}");
        }

        WriteExpression(conversion.Operand);
    }

    private void WriteIncrement(IrIncrement increment)
    {
        using (_writer.Parentheses())
        {
            var step = increment.IsDecrement ? "--" : "++";
            _writer.AppendIf(increment.IsChecked, "checked ");
            _writer.AppendIf(increment.IsPrefix, step);
            WriteExpression(increment.Target);
            _writer.AppendIf(!increment.IsPrefix, step);
        }
    }

    private void WriteStructCreation(IrStructCreation creation)
    {
        _writer.Append($"new {IteratorTypeResolver.FormatType(creation.Type)}");
        using (_writer.Parentheses())
        {
            WriteArguments(creation.Arguments);
        }

        WriteList(creation.Initializers);
    }

    private void WriteArrayCreation(IrArrayCreation array)
    {
        _writer.Append($"new {IteratorTypeResolver.FormatType(array.ElementType)}");
        using (_writer.Brackets())
        {
            WriteExpression(array.Length);
        }

        WriteList(array.Elements);
    }

    private void WriteSequence(IrSequence sequence)
    {
        _writer.Append("seq");
        using (_writer.Parentheses())
        {
            for (var index = 0; index < sequence.Locals.Length; index++)
            {
                _writer.AppendIf(index > 0, ", ");
                _owner.WriteVariable(sequence.Locals[index]);
            }

            _writer.Append("; ");
            foreach (var sideEffect in sequence.SideEffects)
            {
                _writer.Append(_owner.PrintInline(sideEffect));
                _writer.Append("; ");
            }

            WriteExpression(sequence.Value);
        }
    }

    private void WriteOpaque(IrOpaqueExpression opaque)
    {
        _writer.Append($"opaque:{IrPrintText.Reason(opaque.Reason)}");
        using (_writer.Parentheses())
        {
            _writer.Append(IrPrintText.FirstLine(opaque.Syntax));
        }
    }

    private void WriteReceiver(IrExpression? receiver, ITypeSymbol containingType)
    {
        if (receiver is null)
        {
            _writer.Append(IteratorTypeResolver.FormatType(containingType));
            return;
        }

        WriteOperand(receiver);
    }

    // A receiver or an operand used as a location is parenthesized with its category: (ref cell).Peek(), &(ref x)
    private void WriteOperand(IrExpression operand)
    {
        if (operand.Category == IrCategory.Value)
        {
            WriteExpression(operand);
            return;
        }

        using (_writer.Parentheses())
        {
            WriteExpression(operand);
        }
    }

    private void WriteArguments(ImmutableArray<IrArgument> arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            _writer.AppendIf(index > 0, ", ");
            _writer.Append($"{argument.Parameter.Name}: {IrPrintText.ArgumentKind(argument.Kind)}");
            WriteExpression(argument.Value);
        }
    }

    // eval(b, a) after a call whose arguments are not evaluated in parameter order
    private void WriteEvaluationOrder(ImmutableArray<IrArgument> arguments)
    {
        var inOrder = arguments
            .AsStackEnumerable()
            .Select(static (argument, index) => (argument.EvaluationIndex, index))
            .All(static pair => pair.EvaluationIndex == pair.index);
        if (inOrder)
        {
            return;
        }

        _writer.Append(" eval");
        using (_writer.Parentheses())
        {
            for (var position = 0; position < arguments.Length; position++)
            {
                _writer.AppendIf(position > 0, ", ");
                foreach (var argument in arguments)
                {
                    _writer.AppendIf(argument.EvaluationIndex == position, argument.Parameter.Name);
                }
            }
        }
    }

    private void WriteList<T>(ImmutableArray<T> items) where T : IrExpression
    {
        if (items.IsEmpty)
        {
            return;
        }

        _writer.Append(" ");
        using (_writer.Braces())
        {
            for (var index = 0; index < items.Length; index++)
            {
                _writer.AppendIf(index > 0, ", ");
                WriteExpression(items[index]);
            }
        }
    }
}
