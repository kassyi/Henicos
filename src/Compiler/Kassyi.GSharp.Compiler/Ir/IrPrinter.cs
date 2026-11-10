using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Writes an IR tree as text, for tests and for looking at what the builder made. The text is never output.
/// </summary>
/// <remarks>
/// One statement per line, no indentation, braces on their own lines. Every unary, binary and assignment form is in
/// parentheses. A location is written <c>ref x</c>, a read-only location <c>in x</c>, a pointer value <c>ptr p</c>; a
/// receiver or operand with a category is parenthesized with it, <c>(ref cell).Peek()</c>. Arguments are written in
/// parameter order as <c>name: value</c>; a call whose order of evaluation differs ends in
/// <c>eval(names in the order evaluated)</c>.
/// </remarks>
internal sealed class IrPrinter
{
    private readonly SourceWriter _writer;
    private readonly IrPrintOptions _options;
    private readonly Dictionary<IrVariable, int> _temporaries;
    private readonly Dictionary<string, int> _temporaryCounts;
    private readonly IrExpressionPrinter _expressions;

    private IrPrinter(SourceWriter writer, IrPrintOptions options, IrPrinter? outer = null)
    {
        _writer = writer;
        _options = options;
        _temporaries = outer?._temporaries ?? [];
        _temporaryCounts = outer?._temporaryCounts ?? new Dictionary<string, int>(StringComparer.Ordinal);
        _expressions = new IrExpressionPrinter(this, writer, options);
    }

    /// <summary>Writes a method: its signature and body.</summary>
    public static string Print(IrMethod method, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        var printer = new IrPrinter(writer, options);
        writer.Append($"method {IteratorTypeResolver.FormatType(method.Symbol.ContainingType)}.{method.Symbol.Name}");
        using (writer.Parentheses())
        {
            for (var index = 0; index < method.Parameters.Length; index++)
            {
                var parameter = method.Parameters[index];
                writer.AppendIf(index > 0, ", ");
                printer.WriteVariableType(parameter);
                writer.Append(parameter.Name);
            }
        }

        writer.AppendLine();
        printer.WriteStatement(method.Body);
        return writer.ToString();
    }

    /// <summary>Writes a statement; a block takes several lines.</summary>
    public static string Print(IrStatement statement, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, options).WriteStatement(statement);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Writes an expression on one line.</summary>
    public static string Print(IrExpression expression, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, options)._expressions.WriteExpression(expression);
        return writer.ToString();
    }

    private void WriteStatement(IrStatement statement)
    {
        switch (statement)
        {
            case IrBlock block:
                WriteBlock(block);
                break;
            case IrLocalDeclaration declaration:
                WriteDeclaration(declaration);
                break;
            case IrExpressionStatement expression:
                WriteExpressionStatement(expression);
                break;
            case IrReturn @return:
                WriteReturn(@return);
                break;
            case IrIf @if:
                WriteIf(@if);
                break;
            case IrOpaqueStatement opaque:
                _writer.AppendLine(
                    $"opaque:{IrPrintText.Reason(opaque.Reason)}: {IrPrintText.FirstLine(opaque.Syntax)}");
                break;
            default:
                throw new InvalidOperationException("The IR printer has no form for " + statement.GetType().Name + ".");
        }
    }

    private void WriteExpressionStatement(IrExpressionStatement statement)
    {
        _expressions.WriteExpression(statement.Expression);
        _writer.AppendLine();
    }

    private void WriteBlock(IrBlock block)
    {
        _writer.AppendLine("{");
        foreach (var inner in block.Statements)
        {
            WriteStatement(inner);
        }

        _writer.AppendLine("}");
    }

    private void WriteDeclaration(IrLocalDeclaration declaration)
    {
        _writer.Append("local ");
        WriteVariableType(declaration.Variable);
        _writer.Append(declaration.Variable.Name);
        if (declaration.Initializer is { } initializer)
        {
            _writer.Append(" = ");
            _expressions.WriteExpression(initializer);
        }

        _writer.AppendLine();
    }

    private void WriteReturn(IrReturn @return)
    {
        _writer.Append("return");
        if (@return.Value is { } value)
        {
            _writer.Append(" ");
            _expressions.WriteExpression(value);
        }

        _writer.AppendLine();
    }

    private void WriteIf(IrIf @if)
    {
        _writer.Append("if ");
        _expressions.WriteExpression(@if.Condition);
        _writer.AppendLine();
        WriteStatement(@if.Then);
        if (@if.Else is { } @else)
        {
            _writer.AppendLine("else");
            WriteStatement(@else);
        }
    }

    /// <summary>A statement on one line, with the temporaries numbered as in this printer.</summary>
    public string PrintInline(IrStatement statement)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, _options, this).WriteStatement(statement);
        return writer.ToString().Replace("\r", "").Replace('\n', ' ').TrimEnd();
    }

    /// <summary>Writes a variable: its name, with the region of an inlined copy, or a temporary by its
    /// number.</summary>
    public void WriteVariable(IrVariable variable)
    {
        if (variable.Kind != IrVariableKind.Temporary)
        {
            _writer.Append(variable.Name);
            _writer.AppendIf(variable.Region > 0, "@" + variable.Region.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (!_temporaries.TryGetValue(variable, out var number))
        {
            _temporaryCounts.TryGetValue(variable.Name, out number);
            _temporaryCounts[variable.Name] = number + 1;
            _temporaries.Add(variable, number);
        }

        _writer.Append($"${variable.Name}{number}");
    }

    private void WriteVariableType(IrVariable variable) =>
        _writer.Append(
            $"{IrPrintText.RefKindModifier(variable.RefKind)}{IteratorTypeResolver.FormatType(variable.Type)} ");
}
