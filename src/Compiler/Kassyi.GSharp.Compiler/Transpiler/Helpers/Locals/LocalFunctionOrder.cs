namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>Writes the local functions of a statement list in an order and a form that C++ accepts.</summary>
/// <remarks>
/// [WHY] A local function is written as a C++ lambda. C# lets a statement call a local function that is declared
/// later in the same list, and lets a local function call itself or another one declared after it. A lambda cannot be
/// named before its definition, and it cannot call itself through the <c>auto</c> variable that it initializes.
/// <list type="bullet">
///   <item><description>A local function that an earlier statement uses (also through another local function) is
///   written in front of that statement. C# makes sure that every variable it captures is declared and assigned before
///   that use, so the lambda still captures it by reference.</description></item>
///   <item><description>A local function that reaches itself, directly or through others, is written as a group:
///   one lambda per member that takes the lambdas of the group as parameters, and a lambda with the name of the member
///   that passes them. Inside a member, the names of the group are lambdas of the same shape, so the body is written
///   as it is.</description></item>
/// </list>
/// What is used by what, and which local functions form a group, is decided by <see cref="LocalFunctionPlan"/>; this
/// class only walks the plan and writes.
/// </remarks>
internal sealed class LocalFunctionOrder
{
    private const string SelfPrefix = "__gsharp_lf_self_";
    private const string ImplPrefix = "__gsharp_lf_impl_";

    private readonly IReadOnlyList<StatementSyntax> _statements;
    private readonly LocalFunctionPlan _plan;
    private readonly HashSet<int> _written = [];

    private LocalFunctionOrder(IReadOnlyList<StatementSyntax> statements, LocalFunctionPlan plan, int start,
        HashSet<LocalFunctionStatementSyntax>? writtenElsewhere)
    {
        _statements = statements;
        _plan = plan;

        // The ones before start were written by the caller, and the ones in writtenElsewhere are written there
        for (var i = 0; i < statements.Count; i++)
        {
            if (plan.IsFunction(i) && (i < start || IsWrittenElsewhere(statements[i], writtenElsewhere)))
            {
                _written.Add(i);
            }
        }
    }

    private static bool IsWrittenElsewhere(StatementSyntax statement,
        HashSet<LocalFunctionStatementSyntax>? writtenElsewhere) =>
        writtenElsewhere != null && statement is LocalFunctionStatementSyntax function &&
        writtenElsewhere.Contains(function);

    /// <summary>
    /// The writer for <paramref name="statements"/> from <paramref name="start"/> on, or <see langword="null"/> when
    /// the list declares no local function.
    /// </summary>
    /// <param name="statements">The statement list.</param>
    /// <param name="start">The first statement that the caller writes.</param>
    /// <param name="model">The semantic model of the tree of the list.</param>
    /// <param name="writtenElsewhere">The local functions of the list that another writer has written, if any.</param>
    public static LocalFunctionOrder? Create(IReadOnlyList<StatementSyntax> statements, int start,
        SemanticModel model, HashSet<LocalFunctionStatementSyntax>? writtenElsewhere = null) =>
        LocalFunctionPlan.Create(statements, model) is { } plan
            ? new LocalFunctionOrder(statements, plan, start, writtenElsewhere)
            : null;

    /// <summary>
    /// Writes what the local functions of the list need before the statement at <paramref name="index"/>, and the
    /// statement itself when it is a local function. Returns <see langword="true"/> when the statement was written
    /// here or before, so the caller must not write it again.
    /// </summary>
    /// <param name="index">The statement that is about to be written.</param>
    /// <param name="context">The transpile context.</param>
    public bool TryWriteStatement(int index, ref TranspileContext context)
    {
        if (_plan.IsFunction(index) && _written.Contains(index))
        {
            return true;
        }

        // The local functions that the statement uses and that are declared later
        foreach (var used in _plan.Uses(index))
        {
            if (used > index && !_written.Contains(used))
            {
                Write(used, ref context);
            }
        }

        if (!_plan.IsFunction(index))
        {
            return false;
        }

        // Writing the local functions that this one uses may have written its group already
        if (!_written.Contains(index))
        {
            Write(index, ref context);
        }

        return true;
    }

    // The local function with the ones of its list that its group uses first, as a lambda, or as its group
    private void Write(int index, ref TranspileContext context)
    {
        var group = _plan.Group(index);
        foreach (var member in group)
        {
            _written.Add(member);
        }

        foreach (var member in group)
        {
            foreach (var used in _plan.Uses(member))
            {
                if (_plan.IsFunction(used) && !_written.Contains(used))
                {
                    Write(used, ref context);
                }
            }
        }

        if (group.Count == 1 && !_plan.IsRecursive(index))
        {
            context.Visitor.Visit(_statements[index]);
            context.Emitter.Writer.AppendLine();
            return;
        }

        WriteGroup(_statements, group, ref context);
    }

    // auto <impl> = [&](auto& <self a>, auto& <self b>, <parameters>) -> <return type> { <forwarders> <body> };
    // auto <name> = [&](<parameters>) -> <return type> { return <impl>(<impls>, <arguments>); };
    private static void WriteGroup(IReadOnlyList<StatementSyntax> statements, IReadOnlyList<int> group,
        ref TranspileContext context)
    {
        foreach (var member in group)
        {
            WriteImplementation((LocalFunctionStatementSyntax)statements[member], group, statements, ref context);
        }

        foreach (var member in group)
        {
            WriteForwarder((LocalFunctionStatementSyntax)statements[member], group, statements, useSelves: false,
                ref context);
        }
    }

    private static void WriteImplementation(LocalFunctionStatementSyntax function, IReadOnlyList<int> group,
        IReadOnlyList<StatementSyntax> statements, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var returnType = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(function)?.ReturnType;
        writer.Append("auto ");
        AppendGroupName(function, useSelves: false, ref context);
        writer.Append(" = [&](");
        foreach (var other in group)
        {
            writer.Append("auto& ");
            AppendGroupName((LocalFunctionStatementSyntax)statements[other], useSelves: true, ref context);
            writer.Append(", ");
        }

        AppendParameters(function, ref context);
        writer.Append(") -> ");
        context.Emitter.Dialect.AppendType(ref context, returnType);
        writer.AppendLine();
        using (writer.Scope())
        {
            foreach (var other in group)
            {
                WriteForwarder((LocalFunctionStatementSyntax)statements[other], group, statements, useSelves: true,
                    ref context);
            }

            LambdaTranspilerHelper.TranspileLambdaBody(ref context, function.Body,
                function.ExpressionBody?.Expression, returnType is { SpecialType: SpecialType.System_Void });
        }

        writer.AppendLine(";");
    }

    private static void WriteForwarder(LocalFunctionStatementSyntax function, IReadOnlyList<int> group,
        IReadOnlyList<StatementSyntax> statements, bool useSelves, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var returnType = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(function)?.ReturnType;
        writer.Append("auto ");
        CppNameResolver.AppendDeclaredName(function, function.Identifier, ref context);
        writer.Append(" = [&](");
        AppendParameters(function, ref context);
        writer.Append(") -> ");
        context.Emitter.Dialect.AppendType(ref context, returnType);
        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendIf(returnType is not { SpecialType: SpecialType.System_Void }, "return ");
            AppendGroupName(function, useSelves, ref context);
            using (writer.Parentheses())
            {
                foreach (var other in group)
                {
                    AppendGroupName((LocalFunctionStatementSyntax)statements[other], useSelves, ref context);
                    writer.Append(", ");
                }

                AppendArguments(function, ref context);
            }

            writer.AppendLine(";");
        }

        writer.AppendLine(";");
    }

    private static void AppendGroupName(LocalFunctionStatementSyntax function, bool useSelves,
        ref TranspileContext context)
    {
        context.Emitter.Writer.Append(useSelves ? SelfPrefix : ImplPrefix);
        CppNameResolver.AppendDeclaredName(function, function.Identifier, ref context);
    }

    private static void AppendParameters(LocalFunctionStatementSyntax function, ref TranspileContext context)
    {
        var parameters = function.ParameterList.Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                context.Emitter.Writer.Append(", ");
            }

            context.Visitor.Visit(parameters[i]);
        }
    }

    private static void AppendArguments(LocalFunctionStatementSyntax function, ref TranspileContext context)
    {
        var parameters = function.ParameterList.Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                context.Emitter.Writer.Append(", ");
            }

            CppNameResolver.AppendDeclaredName(parameters[i], parameters[i].Identifier, ref context);
        }
    }
}
