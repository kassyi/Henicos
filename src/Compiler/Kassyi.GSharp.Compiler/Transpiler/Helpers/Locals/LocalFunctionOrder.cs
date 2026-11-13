namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>Writes the local functions of a statement list in an order and a form that C++ accepts.</summary>
/// <remarks>
/// [WHY] A local function is written as a C++ lambda. C# lets a statement call a local function that is declared
/// later in the same list, and lets a local function call itself or another one declared after it. A lambda cannot be
/// named before its definition, and it cannot call itself through the <c>auto</c> variable that it initializes.
/// <list type="bullet">
///   <item><description>A local function that an earlier statement uses (also through another local function) is
///   written in front of that statement. C# makes sure that every variable it captures is declared and assigned before
///   that use, so the lambda captures it by reference from there.</description></item>
///   <item><description>A local function that reaches itself, directly or through others, is written as a group:
///   one lambda per member that takes the lambdas of the group as parameters, and a lambda with the name of the member
///   that passes them. Inside a member, the names of the group are lambdas of the same shape, so the body is written
///   as it is.</description></item>
/// </list>
/// </remarks>
internal static class LocalFunctionOrder
{
    private const string SelfPrefix = "__gsharp_lf_self_";
    private const string ImplPrefix = "__gsharp_lf_impl_";

    /// <summary>
    /// The set that tracks the local functions written ahead of their declaration while a statement list is written
    /// from <paramref name="start"/> on, or <see langword="null"/> when the list declares no local function.
    /// </summary>
    public static HashSet<int>? CreateWrittenSet(SyntaxList<StatementSyntax> statements, int start)
    {
        HashSet<int>? written = null;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LocalFunctionStatementSyntax)
            {
                // The ones before start were written by the caller
                written ??= [];
                if (i < start)
                {
                    written.Add(i);
                }
            }
        }

        return written;
    }

    /// <summary>
    /// Writes what the local functions of the list need before the statement at <paramref name="index"/>, and the
    /// statement itself when it is a local function. Returns <see langword="true"/> when the statement was written
    /// here or before, so the caller must not write it again.
    /// </summary>
    /// <param name="statements">The statement list.</param>
    /// <param name="index">The statement that is about to be written.</param>
    /// <param name="written">The set of <see cref="CreateWrittenSet"/>, or <see langword="null"/> for none.</param>
    /// <param name="context">The transpile context.</param>
    public static bool TryWriteStatement(SyntaxList<StatementSyntax> statements, int index, ISet<int>? written,
        ref TranspileContext context)
    {
        if (written == null)
        {
            return false;
        }

        if (IsWritten(statements, index, written))
        {
            return true;
        }

        WriteUsedAhead(statements, index, written, ref context);
        if (statements[index] is not LocalFunctionStatementSyntax)
        {
            return false;
        }

        // Writing the local functions that this one uses may have written its group already
        if (!IsWritten(statements, index, written))
        {
            Write(statements, index, written, ref context);
        }

        return true;
    }

    /// <summary>
    /// Writes the local functions that <paramref name="statements"/>[<paramref name="index"/>] uses before they are
    /// declared.
    /// </summary>
    /// <param name="statements">The statement list.</param>
    /// <param name="index">The statement that is about to be written.</param>
    /// <param name="written">The indices of the local functions that are written already; this method fills it.</param>
    /// <param name="context">The transpile context.</param>
    public static void WriteUsedAhead(SyntaxList<StatementSyntax> statements, int index, ISet<int> written,
        ref TranspileContext context)
    {
        for (var later = index + 1; later < statements.Count; later++)
        {
            if (statements[later] is LocalFunctionStatementSyntax && !written.Contains(later) &&
                IsUsedBy(statements[later], statements[index], ref context))
            {
                Write(statements, later, written, ref context);
            }
        }
    }

    /// <summary>Whether the local function at <paramref name="index"/> was written already.</summary>
    public static bool IsWritten(SyntaxList<StatementSyntax> statements, int index, ISet<int> written) =>
        statements[index] is LocalFunctionStatementSyntax && written.Contains(index);

    /// <summary>
    /// Writes the local function at <paramref name="index"/> with the local functions of its list that it uses first,
    /// as a lambda, or with its group when it is recursive.
    /// </summary>
    /// <param name="statements">The statement list.</param>
    /// <param name="index">The local function statement.</param>
    /// <param name="written">The indices of the local functions that are written already; this method fills it.</param>
    /// <param name="context">The transpile context.</param>
    public static void Write(SyntaxList<StatementSyntax> statements, int index, ISet<int> written,
        ref TranspileContext context)
    {
        var group = FindGroup(statements, index, ref context);
        foreach (var member in group)
        {
            written.Add(member);
        }

        // The local functions that the members use and that are declared later come first
        foreach (var member in group)
        {
            for (var other = 0; other < statements.Count; other++)
            {
                if (statements[other] is LocalFunctionStatementSyntax && !written.Contains(other) &&
                    IsUsedBy(statements[other], statements[member], ref context))
                {
                    Write(statements, other, written, ref context);
                }
            }
        }

        if (group.Count == 1 && !Reaches(statements, index, index, ref context))
        {
            context.Visitor.Visit(statements[index]);
            context.Emitter.Writer.AppendLine();
            return;
        }

        WriteGroup(statements, group, ref context);
    }

    // auto <impl> = [&](auto& <self a>, auto& <self b>, <parameters>) -> <return type> { <forwarders> <body> };
    // auto <name> = [&](<parameters>) -> <return type> { return <impl>(<impls>, <arguments>); };
    private static void WriteGroup(SyntaxList<StatementSyntax> statements, List<int> group,
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

    private static void WriteImplementation(LocalFunctionStatementSyntax function, List<int> group,
        SyntaxList<StatementSyntax> statements, ref TranspileContext context)
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

    private static void WriteForwarder(LocalFunctionStatementSyntax function, List<int> group,
        SyntaxList<StatementSyntax> statements, bool useSelves, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var writer = context.Emitter.Writer;
        var returnType = model.GetDeclaredSymbol<IMethodSymbol>(function)?.ReturnType;
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

    // The local functions of the list that the function at index reaches and that reach it, itself included
    private static List<int> FindGroup(SyntaxList<StatementSyntax> statements, int index, ref TranspileContext context)
    {
        List<int> group = [index];
        for (var other = 0; other < statements.Count; other++)
        {
            if (other != index && statements[other] is LocalFunctionStatementSyntax &&
                Reaches(statements, index, other, ref context) && Reaches(statements, other, index, ref context))
            {
                group.Add(other);
            }
        }

        group.Sort();
        return group;
    }

    // Whether the local function at from uses the one at to, directly or through other local functions
    private static bool Reaches(SyntaxList<StatementSyntax> statements, int from, int to, ref TranspileContext context)
    {
        HashSet<int> seen = [];
        Stack<int> pending = new();
        pending.Push(from);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            for (var next = 0; next < statements.Count; next++)
            {
                if (statements[next] is not LocalFunctionStatementSyntax ||
                    !IsUsedBy(statements[next], statements[current], ref context))
                {
                    continue;
                }

                if (next == to)
                {
                    return true;
                }

                if (seen.Add(next))
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    private static bool IsUsedBy(StatementSyntax function, StatementSyntax user, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var name = ((LocalFunctionStatementSyntax)function).Identifier.ValueText;
        var symbol = model.GetDeclaredSymbol<ISymbol>(function);
        var region = LoweringIdentity.GetRegion(function);
        foreach (var node in user.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == name &&
                LoweringIdentity.GetRegion(identifier) == region &&
                (symbol == null || model.GetSymbol(identifier).IsEqualTo(symbol)))
            {
                return true;
            }
        }

        return false;
    }
}
