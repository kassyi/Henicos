using Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a <c>throw</c> statement and a <c>throw</c> expression of device code into a failed check: the failure is
/// recorded with the exception type and the place of the throw, and the thread of the CPU paths and of CUDA ends there
/// (<c>__gsharp_trap_abort</c>), as C# leaves the method through the exception. The launch then throws an exception of
/// the same type.
/// </summary>
/// <remarks>
/// <para>
/// Device code holds no exception object, so the exception is the <c>new</c> expression in place of the throw; its
/// arguments are evaluated first, in C# order, and then dropped, since the failure keeps only the type, the place and a
/// message that is a literal. An exception type that <c>GSharpErrorCode</c> has a code for maps to it; any other type is
/// recorded by its name in the error site (<c>GSharpErrorCode::Throw</c>), which the host builds an exception from. A
/// function without an error site table keeps the message and the place in the message instead.
/// </para>
/// </remarks>
internal static class ThrowTranspiler
{
    /// <summary>The GSH0003 text for a <c>throw</c> whose operand is not a <c>new</c> exception.</summary>
    public const string ThrowOfExistingException = "throw of an exception that is not created in the throw";

    /// <summary>How a <c>throw</c> is written, decided before anything is written (Plan / Emit).</summary>
    /// <param name="ErrorCode">The <c>GSharpErrorCode</c> member.</param>
    /// <param name="Message">The literal message of the exception, or empty.</param>
    /// <param name="ExceptionType">The assembly-qualified type for <c>Throw</c>, or empty.</param>
    /// <param name="EvaluatedArguments">The arguments to evaluate before the failure, in order.</param>
    internal readonly record struct ThrowPlan(
        string ErrorCode,
        string Message,
        string ExceptionType,
        ImmutableArray<ExpressionSyntax> EvaluatedArguments);

    /// <summary>Writes a <c>throw</c> statement.</summary>
    public static void TranspileStatement(ThrowStatementSyntax node, ref TranspileContext context)
    {
        if (!TryPlan(node, node.Expression, ref context, out var plan))
        {
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        var writer = context.Emitter.Writer;
        if (plan.EvaluatedArguments.IsEmpty)
        {
            AppendTrap(node, plan, ref context);
            writer.Append(";");
            return;
        }

        using (writer.Scope())
        {
            AppendArguments(plan, ref context);
            AppendTrap(node, plan, ref context);
            writer.AppendLine(";");
        }
    }

    /// <summary>Writes a <c>throw</c> expression: an immediately invoked lambda that has the type of its context.</summary>
    public static void TranspileExpression(ThrowExpressionSyntax node, ref TranspileContext context)
    {
        if (!TryPlan(node, node.Expression, ref context, out var plan))
        {
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        var writer = context.Emitter.Writer;
        var type = ContextType(node, context.Query.SemanticModel);
        writer.Append("([&]()");
        if (type is { SpecialType: not SpecialType.System_Void } and not IErrorTypeSymbol)
        {
            writer.Append(" -> ");
            context.Emitter.Dialect.AppendType(ref context, type);
        }

        writer.Append(" ");
        using (writer.Scope())
        {
            AppendArguments(plan, ref context);
            AppendTrap(node, plan, ref context);
            writer.AppendLine(";");
        }

        writer.Append(")()");
    }

    // (void)(<argument>); for each argument that is more than a literal
    private static void AppendArguments(ThrowPlan plan, ref TranspileContext context)
    {
        foreach (var argument in plan.EvaluatedArguments)
        {
            context.Emitter.Writer.Append("(void)");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(argument);
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    // __gsharp_trap_abort(GSharpErrorCode::<code>, <site>u), or, in a function without a site table, with a message
    private static void AppendTrap(SyntaxNode node, ThrowPlan plan, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append($"{GSharpInternalNames.Prelude.TrapAbort}(GSharpErrorCode::{plan.ErrorCode}, ");
        if (context.Emitter.ErrorSites is { } table)
        {
            var site = table.Register(node, context.Query.SemanticModel, context.Emitter.CurrentMemberName ?? "",
                plan.Message, "Throw", plan.ExceptionType);
            writer.Append($"{site}u");
        }
        else
        {
            var place = SourcePlace.Format(context.Query.SemanticModel, node);
            var text = plan.Message.Length > 0 ? plan.Message : "An exception was thrown";
            CppStringLiteral.Append(writer, $"{text} at {place}");
        }

        writer.Append(")");
    }

    private static bool TryPlan(SyntaxNode node, ExpressionSyntax? operand, ref TranspileContext context,
        out ThrowPlan plan)
    {
        plan = default;
        var model = context.Query.SemanticModel;
        if (operand is not BaseObjectCreationExpressionSyntax creation ||
            model.GetTypeSymbol(creation) is not INamedTypeSymbol type ||
            !IsExceptionType(type, model.Compilation))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ThrowOfExistingException);
            return false;
        }

        var arguments = creation.ArgumentList?.Arguments ?? default;
        var code = ErrorCodeOf(type);
        var typeName = code == "Throw"
            ? FullName(type) + ", " + type.ContainingAssembly.Name
            : "";
        var evaluated = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        foreach (var argument in arguments)
        {
            if (!IsLiteralOrNameof(argument.Expression))
            {
                evaluated.Add(argument.Expression);
            }
        }

        plan = new ThrowPlan(code, MessageOf(type, arguments), typeName, evaluated.ToImmutable());
        return true;
    }

    // The type that the context gives a throw expression: the other branch of ?: and the value of ?? (read from the
    // sibling, since a lowered tree has no type information of its own for the throw); null in any other position
    private static ITypeSymbol? ContextType(ThrowExpressionSyntax node, SemanticModel model)
    {
        switch (node.Parent)
        {
            case ConditionalExpressionSyntax conditional:
                return model.GetTypeSymbol(conditional.WhenTrue == node ? conditional.WhenFalse : conditional.WhenTrue);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right == node:
                var left = model.GetTypeSymbol(binary.Left);
                return left is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                    ? nullable.TypeArguments[0]
                    : left;
            default:
                return null;
        }
    }

    /// <summary>Whether <paramref name="type"/> is <see cref="Exception"/> or derives from it.</summary>
    public static bool IsExceptionType(ITypeSymbol? type, Compilation compilation)
    {
        var exception = compilation.GetTypeByMetadataName("System.Exception");
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, exception))
            {
                return true;
            }
        }

        return false;
    }

    // A literal or nameof(...) argument has nothing to evaluate
    private static bool IsLiteralOrNameof(ExpressionSyntax expression) => expression is LiteralExpressionSyntax or
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };

    // The message of the exception when it is a literal. The two types whose first string is a parameter name carry the
    // message last; every other type with a message takes it first
    private static string MessageOf(INamedTypeSymbol type, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var paramNameFirst = type.Name is "ArgumentOutOfRangeException" or "ArgumentNullException";
        if (arguments.Count == 0 || (paramNameFirst && arguments.Count == 1))
        {
            return "";
        }

        var candidate = paramNameFirst ? arguments[arguments.Count - 1] : arguments[0];
        return candidate.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : "";
    }

    // Namespace and name, without type arguments or the global:: prefix
    private static string FullName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.Name : type.Name;

    private static string ErrorCodeOf(INamedTypeSymbol type) =>
        FullName(type) switch
        {
            "System.NotSupportedException" => "NotSupported",
            "System.InvalidOperationException" => "InvalidOperation",
            "System.IndexOutOfRangeException" => "IndexOutOfRange",
            "System.ArgumentOutOfRangeException" => "ArgumentOutOfRange",
            "System.ArgumentException" => "Argument",
            "System.NullReferenceException" => "NullReference",
            "System.DivideByZeroException" => "DivideByZero",
            "System.OverflowException" => "Overflow",
            "System.ArgumentNullException" => "ArgumentNull",
            "System.Runtime.CompilerServices.SwitchExpressionException" => "SwitchExpression",
            _ => "Throw"
        };
}
