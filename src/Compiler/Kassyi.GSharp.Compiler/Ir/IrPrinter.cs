using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
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
    private readonly bool _oneLine;
    private bool _separatorPending;
    private int _indent;

    private IrPrinter(SourceWriter writer, IrPrintOptions options, IrPrinter? outer = null, bool oneLine = false)
    {
        _writer = writer;
        _oneLine = oneLine;
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

    /// <summary>
    /// Writes statements one after another, with the temporaries numbered across them (the output of a pass that
    /// replaces one statement with several).
    /// </summary>
    public static string Print(ImmutableArray<IrStatement> statements, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        var printer = new IrPrinter(writer, options);
        foreach (var statement in statements)
        {
            printer.WriteStatement(statement);
        }

        return writer.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Writes an expression on one line.</summary>
    public static string Print(IrExpression expression, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, options)._expressions.WriteExpression(expression);
        return writer.ToString();
    }

    /// <summary>Writes one state of a state machine with its statements indented.</summary>
    public static string Print(IrState state, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, options).WriteState(state);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Writes all states of a state machine.</summary>
    public static string Print(IrStateMachine machine, IrPrintOptions options = IrPrintOptions.None)
    {
        using var writer = new SourceWriter();
        var printer = new IrPrinter(writer, options);
        foreach (var state in machine.States)
        {
            printer.WriteState(state);
        }

        return writer.ToString().TrimEnd('\r', '\n');
    }

    private void WriteState(IrState state)
    {
        StartLine();
        _writer.Append($"case {state.Number.ToString(CultureInfo.InvariantCulture)}:");
        EndLine();
        _indent++;
        foreach (var statement in state.Statements)
        {
            WriteStatement(statement);
        }

        _indent--;
    }

    private void WriteStatement(IrStatement statement)
    {
        StartLine();
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
            case IrWhile @while:
                WriteWhile(@while);
                break;
            case IrOpaqueStatement opaque:
                WriteOpaque(opaque);
                break;
            case IrLabel label:
                WriteLabel(label);
                break;
            case IrGoto @goto:
                WriteGoto(@goto);
                break;
            case IrConditionalGoto conditionalGoto:
                WriteConditionalGoto(conditionalGoto);
                break;
            case IrSuspend suspend:
                WriteSuspend(suspend);
                break;
            case IrResumeCall resume:
                WriteResumeCall(resume);
                break;
            case IrStateJump jump:
                WriteStateJump(jump);
                break;
            case IrStateMachineExit:
                _writer.Append("exit;");
                EndLine();
                break;
            default:
                throw new InvalidOperationException("The IR printer has no form for " + statement.GetType().Name + ".");
        }
    }

    private void WriteLabel(IrLabel label)
    {
        _writer.Append($"{label.Label.Name}: ;");
        EndLine();
    }

    private void WriteGoto(IrGoto @goto)
    {
        _writer.Append($"goto {@goto.Target.Name};");
        EndLine();
    }

    private void WriteConditionalGoto(IrConditionalGoto conditionalGoto)
    {
        WriteConditionBranchPrefix(conditionalGoto.Condition, conditionalGoto.JumpIfTrue);
        _writer.Append($" goto {conditionalGoto.Target.Name};");
        EndLine();
    }

    private void WriteSuspend(IrSuspend suspend)
    {
        _writer.Append(
            $"suspend {suspend.Kind.ToString()} -> {suspend.ResumeState.ToString(CultureInfo.InvariantCulture)};");
        EndLine();
    }

    private void WriteResumeCall(IrResumeCall resume)
    {
        _writer.Append($"resume {resume.CallFrame.ToString(CultureInfo.InvariantCulture)} ");
        _expressions.WriteExpression(resume.Call);
        if (resume.ResultTarget is { } target)
        {
            _writer.Append(" -> ");
            _expressions.WriteExpression(target);
        }

        _writer.Append($" @{resume.State.ToString(CultureInfo.InvariantCulture)};");
        EndLine();
    }

    private void WriteStateJump(IrStateJump jump)
    {
        if (jump.Condition is { } condition)
        {
            WriteConditionBranchPrefix(condition, jump.JumpIfTrue);
            _writer.Append(" ");
        }

        _writer.Append($"goto case {jump.State.ToString(CultureInfo.InvariantCulture)};");
        EndLine();
    }

    private void WriteConditionBranchPrefix(IrExpression condition, bool jumpIfTrue)
    {
        _writer.Append("if ");
        using (_writer.Parentheses())
        {
            if (!jumpIfTrue)
            {
                _writer.Append("!");
                using (_writer.Parentheses())
                {
                    _expressions.WriteExpression(condition);
                }
            }
            else
            {
                _expressions.WriteExpression(condition);
            }
        }
    }

    private void WriteOpaque(IrOpaqueStatement opaque)
    {
        _writer.Append($"opaque:{IrPrintText.Reason(opaque.Reason)}: {IrPrintText.SyntaxKindName(opaque.Syntax)}");
        EndLine();
    }

    private void WriteExpressionStatement(IrExpressionStatement statement)
    {
        _expressions.WriteExpression(statement.Expression);
        EndLine();
    }

    private void WriteBlock(IrBlock block)
    {
        _writer.Append("{");
        EndLine();
        foreach (var inner in block.Statements)
        {
            WriteStatement(inner);
        }

        StartLine();
        _writer.Append("}");
        EndLine();
    }

    private void WriteDeclaration(IrLocalDeclaration declaration)
    {
        _writer.Append("local ");
        WriteVariableType(declaration.Variable);
        if (declaration.Variable.Kind == IrVariableKind.Temporary)
        {
            WriteVariable(declaration.Variable);
        }
        else
        {
            _writer.Append(declaration.Variable.Name);
        }

        if (declaration.Initializer is { } initializer)
        {
            _writer.Append(" = ");
            _expressions.WriteExpression(initializer);
        }

        EndLine();
    }

    private void WriteReturn(IrReturn @return)
    {
        _writer.Append("return");
        if (@return.Value is { } value)
        {
            _writer.Append(" ");
            _expressions.WriteExpression(value);
        }

        EndLine();
    }

    private void WriteIf(IrIf @if)
    {
        _writer.Append("if ");
        _expressions.WriteExpression(@if.Condition);
        EndLine();
        WriteStatement(@if.Then);
        if (@if.Else is { } @else)
        {
            StartLine();
            _writer.Append("else");
            EndLine();
            WriteStatement(@else);
        }
    }

    private void WriteWhile(IrWhile @while)
    {
        _writer.Append("while ");
        _expressions.WriteExpression(@while.Condition);
        EndLine();
        WriteStatement(@while.Body);
    }

    /// <summary>A statement on one line, with the temporaries numbered as in this printer.</summary>
    public string PrintInline(IrStatement statement)
    {
        using var writer = new SourceWriter();
        new IrPrinter(writer, _options, this, oneLine: true).WriteStatement(statement);
        return writer.ToString();
    }

    // Ends a line; on one line, the next line starts after a space instead
    private void EndLine()
    {
        if (_oneLine)
        {
            _separatorPending = true;
        }
        else
        {
            _writer.AppendLine();
        }
    }

    private void StartLine()
    {
        _writer.AppendIf(_separatorPending, " ");
        _separatorPending = false;
        for (var i = 0; i < _indent; i++)
        {
            _writer.Append("    ");
        }
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
