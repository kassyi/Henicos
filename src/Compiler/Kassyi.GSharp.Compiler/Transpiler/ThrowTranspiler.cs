using System.Text;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;
using Kassyi.GSharp.Compiler.Errors;

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
/// arguments are evaluated first, in C# order, and then dropped, since the failure keeps only the type, the place, a
/// message that is a constant string (a literal, a <c>const</c>, <c>nameof</c>, a constant interpolation) and its
/// parameter name, which travels as the suffix <c> (Parameter 'x')</c> of the message. An exception type that
/// <see cref="ThrowableExceptions"/> lists maps to its <c>GSharpErrorCode</c>; any other type is recorded by its CLR
/// name in the error site (<c>GSharpErrorCode::Throw</c>), which the host builds the exception from, so a type the host
/// cannot build (abstract, no public constructor of <c>(string, Exception)</c>, <c>(string)</c> or <c>()</c>) is GSH0003.
/// A device function has no error site table: it keeps the message and the place in the message, and a type that needs
/// the table is GSH0003 there. A throw inside the scope of a <c>using</c> disposes the guards on CUDA
/// (<see cref="UnwindingTrap"/>).
/// </para>
/// </remarks>
internal static class ThrowTranspiler
{
    /// <summary>The GSH0003 text for a <c>throw</c> whose operand is not a <c>new</c> exception.</summary>
    public const string ThrowOfExistingException = "throw of an exception that is not created in the throw";

    /// <summary>The GSH0003 text for a <c>throw</c> expression whose type the position does not give.</summary>
    public const string ThrowExpressionWithoutType = "throw expression in a position that gives it no type";

    /// <summary>The GSH0003 text for a string argument of the exception that is not a constant.</summary>
    public const string ThrowTextNotConstant =
        "string argument of a thrown exception that is not a constant (a string has no device form)";

    /// <summary>The GSH0003 text for an argument of the exception that is an object.</summary>
    public const string ThrowObjectArgument = "argument of a thrown exception that is an object (it has no device form)";

    /// <summary>The GSH0003 text for an exception type that the host cannot build.</summary>
    public const string ThrowTypeNotBuildable =
        "exception type that the host cannot build (it must be a non-abstract type with a public constructor of (string, Exception), (string) or ())";

    /// <summary>The GSH0003 text for an exception type with no code of its own thrown in a device function.</summary>
    public const string ThrowTypeInDeviceFunction =
        "throw of an exception type without an error code in a device function (it has no error site table to name the type)";

    /// <summary>How a <c>throw</c> is written, decided before anything is written (Plan / Emit).</summary>
    /// <param name="ErrorCode">The <c>GSharpErrorCode</c> member.</param>
    /// <param name="ClrName">The assembly-qualified CLR type name for <c>Throw</c>, or empty.</param>
    /// <param name="Text">The constant message of the exception, or empty.</param>
    /// <param name="ParameterSuffix">The <c> (Parameter 'x')</c> suffix of the message, or empty.</param>
    /// <param name="EvaluatedArguments">The arguments to evaluate before the failure, in order.</param>
    internal readonly record struct ThrowPlan(
        string ErrorCode,
        string ClrName,
        string Text,
        string ParameterSuffix,
        ImmutableArray<ExpressionSyntax> EvaluatedArguments)
    {
        /// <summary>What the error site carries: the type, and the message with its suffix.</summary>
        public ThrownException Thrown => new(ClrName, Text + ParameterSuffix);
    }

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

        var type = ContextType(node, context.Query.SemanticModel);
        if (type is null or IErrorTypeSymbol)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ThrowExpressionWithoutType);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        var writer = context.Emitter.Writer;
        writer.Append("([&]()");
        if (type.SpecialType != SpecialType.System_Void)
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

    // (void)(<argument>); for each argument that is not a constant
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

    // __gsharp_trap_abort(GSharpErrorCode::<code>, <site>u): the call of every failed check, which on CUDA inside a
    // using scope also disposes the guards (UnwindingTrap). A function without a site table passes the text and the place
    private static void AppendTrap(SyntaxNode node, ThrowPlan plan, ref TranspileContext context)
    {
        ErrorSiteArgument site;
        if (context.Emitter.ErrorSites is { } table)
        {
            site = ErrorSiteArgument.FromSiteId(table.RegisterThrow(
                node, context.Query.SemanticModel, context.Emitter.CurrentMemberName ?? "", plan.Thrown));
        }
        else
        {
            var place = SourcePlace.Format(context.Query.SemanticModel, node);
            // The place goes before the suffix, which the host reads from the end of the text
            var text = plan.Text.Length > 0 ? plan.Text : "An exception was thrown";
            site = ErrorSiteArgument.FromMessage($"{text} at {place}{plan.ParameterSuffix}");
        }

        UnwindingTrap.Append(ref context, GSharpInternalNames.Prelude.TrapAbort, plan.ErrorCode, site);
    }

    private static bool TryPlan(SyntaxNode node, ExpressionSyntax? operand, ref TranspileContext context,
        out ThrowPlan plan)
    {
        plan = default;
        var model = context.Query.SemanticModel;
        if (operand is not BaseObjectCreationExpressionSyntax creation ||
            model.GetTypeSymbol(creation) is not INamedTypeSymbol type ||
            model.GetSymbol(creation) is not IMethodSymbol constructor ||
            !IsExceptionType(type, model.Compilation))
        {
            return Reject(ref context, node, ThrowOfExistingException);
        }

        var code = "Throw";
        var clrName = "";
        ThrowableException entry = default;
        var mapped = type.ContainingType is null && !type.IsGenericType &&
                     ThrowableExceptions.TryFindByFullName(QualifiedName(type), out entry);
        if (mapped)
        {
            code = entry.CodeName;
        }
        else if (!IsHostBuildable(type) || !TryClrName(type, out clrName))
        {
            return Reject(ref context, node, ThrowTypeNotBuildable);
        }
        else if (context.Emitter.IsDeviceFunction)
        {
            return Reject(ref context, node, ThrowTypeInDeviceFunction);
        }

        var message = "";
        var paramName = "";
        var evaluated = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        var arguments = creation.ArgumentList?.Arguments ?? default;
        for (var i = 0; i < arguments.Count; i++)
        {
            var expression = arguments[i].Expression;
            var parameter = ParameterOf(constructor, arguments[i], i);
            var constant = model.GetSafeConstantValue(expression);
            if (constant.HasValue)
            {
                var text = constant.Value as string ?? "";
                if (parameter?.Name == "message")
                {
                    message = text;
                }
                else if (parameter?.Name == "paramName")
                {
                    paramName = text;
                }

                continue;
            }

            var argumentType = model.GetTypeSymbol(expression);
            if (argumentType?.SpecialType == SpecialType.System_String)
            {
                return Reject(ref context, expression, ThrowTextNotConstant);
            }

            if (argumentType is { IsReferenceType: true })
            {
                return Reject(ref context, expression, ThrowObjectArgument);
            }

            evaluated.Add(expression);
        }

        // new ArgumentException("m", "p").Message is "m (Parameter 'p')"
        var suffix = "";
        if (mapped && entry.HasParameterName && paramName.Length > 0)
        {
            suffix = $" (Parameter '{paramName}')";
            if (message.Length == 0)
            {
                message = entry.DefaultMessage;
            }
        }

        plan = new ThrowPlan(code, clrName, message, suffix, evaluated.ToImmutable());
        return true;
    }

    private static bool Reject(ref TranspileContext context, SyntaxNode node, string text)
    {
        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, text);
        return false;
    }

    // The parameter an argument is passed to: by name, else by position (the last parameter for a params array)
    private static IParameterSymbol? ParameterOf(IMethodSymbol constructor, ArgumentSyntax argument, int index)
    {
        if (argument.NameColon is { } nameColon)
        {
            return constructor.Parameters
                .AsStackEnumerable()
                .FirstOrDefault(nameColon.Name.Identifier.ValueText, static (p, name) => p.Name == name);
        }

        var parameters = constructor.Parameters;
        if (index < parameters.Length)
        {
            return parameters[index];
        }

        return parameters.Length > 0 && parameters[parameters.Length - 1].IsParams ? parameters[parameters.Length - 1] : null;
    }

    // The type that the position gives a throw expression, read from the original tree (a lowered tree has no type
    // information of its own for the throw); null in a position that gives none
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
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                return model.GetTypeSymbol(assignment.Left);
            case SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switchExpression }:
                return model.GetTypeSymbol(switchExpression) ?? model.GetConvertedTypeSymbol(switchExpression);
            case ArrowExpressionClauseSyntax arrow:
                return ArrowType(arrow, model);
            case SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax:
                return model.GetSymbol(node.Parent) is IMethodSymbol lambda ? lambda.ReturnType : null;
            default:
                return null;
        }
    }

    // The type of the value of an expression body: the return type of a method or local function, the type of a property
    private static ITypeSymbol? ArrowType(ArrowExpressionClauseSyntax arrow, SemanticModel model) =>
        arrow.Parent switch
        {
            MethodDeclarationSyntax or LocalFunctionStatementSyntax =>
                model.GetDeclaredSymbol<IMethodSymbol>(arrow.Parent)?.ReturnType,
            PropertyDeclarationSyntax or IndexerDeclarationSyntax =>
                model.GetDeclaredSymbol<IPropertySymbol>(arrow.Parent)?.Type,
            _ => null
        };

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

    // Namespace and name of a type that is not nested or generic
    private static string QualifiedName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.Name : type.Name;

    // What the host can do with the type (GSharpErrorMapper.InstantiateThrown): a concrete type that has a public
    // constructor of (string, Exception), (string) or ()
    private static bool IsHostBuildable(INamedTypeSymbol type)
    {
        if (type.IsAbstract)
        {
            return false;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var parameters = constructor.Parameters;
            if (parameters.Length == 0 ||
                (parameters.Length == 1 && parameters[0].Type.SpecialType == SpecialType.System_String) ||
                (parameters.Length == 2 && parameters[0].Type.SpecialType == SpecialType.System_String &&
                 parameters[1].Type.Name == "Exception"))
            {
                return true;
            }
        }

        return false;
    }

    // The assembly-qualified name that Type.GetType reads: Ns.Outer+Nested`1[[Argument, Assembly]], Assembly
    private static bool TryClrName(INamedTypeSymbol type, out string name)
    {
        var builder = new StringBuilder();
        var ok = TryAppendQualified(type, builder);
        name = ok ? builder.ToString() : "";
        return ok;
    }

    private static bool TryAppendQualified(INamedTypeSymbol type, StringBuilder builder)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            chain.Insert(0, current);
        }

        if (chain[0].ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            builder.Append(ns.ToDisplayString()).Append('.');
        }

        var arguments = new List<ITypeSymbol>();
        for (var i = 0; i < chain.Count; i++)
        {
            builder.Append(i > 0 ? "+" : "").Append(chain[i].Name);
            if (chain[i].Arity > 0)
            {
                builder.Append('`').Append(chain[i].Arity);
            }

            arguments.AddRange(chain[i].TypeArguments);
        }

        if (arguments.Count > 0)
        {
            builder.Append('[');
            for (var i = 0; i < arguments.Count; i++)
            {
                if (arguments[i] is not INamedTypeSymbol argument)
                {
                    return false;
                }

                builder.Append(i > 0 ? ",[" : "[");
                if (!TryAppendQualified(argument, builder))
                {
                    return false;
                }

                builder.Append(']');
            }

            builder.Append(']');
        }

        builder.Append(", ").Append(type.ContainingAssembly.Name);
        return true;
    }
}
