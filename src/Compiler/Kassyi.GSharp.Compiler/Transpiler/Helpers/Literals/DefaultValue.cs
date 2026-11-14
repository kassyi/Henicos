namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;

/// <summary>How a <c>default(T)</c> or <c>default</c> is written in C++.</summary>
internal enum DefaultValueForm
{
    /// <summary><c>{}</c>, valid where a braced list may stand.</summary>
    Braced,

    /// <summary>The typed value <c>__gsharp_type&lt;T&gt;{}</c>.</summary>
    Typed
}

/// <summary>
/// The decision how a <c>default</c> is written: its form and, for <see cref="DefaultValueForm.Typed"/>, <c>T</c>, and
/// the <c>Nullable&lt;U&gt;</c> it converts to when <c>U</c> is another type (<c>long? l = default(int)</c>).
/// </summary>
internal readonly record struct DefaultValuePlan(
    DefaultValueForm Form,
    ITypeSymbol? Type,
    INamedTypeSymbol? NullableTarget = null);

/// <summary>
/// <c>default(T)</c> and the <c>default</c> literal. <c>{}</c> is C++ only where a braced list may stand: the
/// initializer of a declaration with a written type (<c>T x = {};</c>), the right side of a simple assignment
/// (<c>x = {};</c>) and an argument the compiler built itself. Every other position gets
/// <c>__gsharp_type&lt;T&gt;{}</c> with <c>T</c> from the semantic model (<c>c ? a : __gsharp_type&lt;int&gt;{}</c>, <c>x == __gsharp_type&lt;int&gt;{}</c>,
/// <c>auto z = __gsharp_type&lt;int&gt;{}</c>, <c>__gsharp_type&lt;S&gt;{}.Field</c>). The alias template is one name
/// for every type, so a type of several words (<c>long long</c>, <c>unsigned char</c>) or a pointer is a valid
/// functional cast in every compiler.
/// </summary>
internal static class DefaultValue
{
    /// <summary>
    /// Decides how <paramref name="node"/> (<c>default(T)</c> or the <c>default</c> literal) is written.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The position needs a type and the semantic model has none for the node.
    /// </exception>
    public static DefaultValuePlan Plan(ExpressionSyntax node, SemanticModel model)
    {
        var type = node is DefaultExpressionSyntax
            ? model.GetTypeSymbol(node)
            : model.GetConvertedTypeSymbol(node);
        // {} initializes the target, so it is the value of the default only when the target has the default's own type:
        // int? q = {} is null, and int? q = default(int) is 0, which __gsharp_type<int>{} converts to
        if (IsBracedListPosition(node) && (node is not DefaultExpressionSyntax || type is null ||
                                           type.IsEqualTo(model.GetConvertedTypeSymbol(node))))
        {
            return new DefaultValuePlan(DefaultValueForm.Braced, null);
        }

        if (type != null)
        {
            // A value of another type than the underlying type of the nullable it converts to is converted to that type
            // first, which is one conversion for C++ to pick: int{} could also be read as a null pointer constant
            var target = node is DefaultExpressionSyntax
                ? model.GetConvertedTypeSymbol(node) as INamedTypeSymbol
                : null;
            return new DefaultValuePlan(
                DefaultValueForm.Typed,
                type,
                target is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] } &&
                !underlying.IsEqualTo(type) && !target.IsEqualTo(type)
                    ? target
                    : null);
        }

        // An omitted optional argument is built by the compiler without a position in the source (the original-span
        // annotation), and a braced list is valid as the argument of a call
        if (node.Parent is ArgumentSyntax)
        {
            return new DefaultValuePlan(DefaultValueForm.Braced, null);
        }

        throw new InvalidOperationException($"The type of '{node}' did not resolve, so its value cannot be written.");
    }

    /// <summary>Writes <paramref name="node"/> as planned by <see cref="Plan"/>.</summary>
    public static void Append(ExpressionSyntax node, ref TranspileContext context)
    {
        var plan = Plan(node, context.Query.SemanticModel);
        if (plan.Type is not { } type)
        {
            context.Emitter.Writer.Append("{}");
            return;
        }

        // <Nullable<U>>(static_cast<U>(<value>)) for a nullable of another underlying type, otherwise <value>
        var target = plan.NullableTarget;
        if (target is not null)
        {
            context.Emitter.Dialect.AppendType(ref context, target);
            context.Emitter.Writer.Append("(static_cast<");
            context.Emitter.Dialect.AppendType(ref context, target.TypeArguments[0]);
            context.Emitter.Writer.Append(">(");
        }

        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TypedValue);
        context.Emitter.Writer.Append('<');
        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.Append(">{}");
        if (target is not null)
        {
            context.Emitter.Writer.Append("))");
        }
    }

    /// <summary>
    /// Whether <paramref name="node"/> is the initializer of a declaration with a written type (not <c>var</c>), the
    /// right side of a simple assignment, or an argument the compiler built itself, where <c>{}</c> is valid C++.
    /// </summary>
    internal static bool IsBracedListPosition(ExpressionSyntax node) =>
        node.Parent switch
        {
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }
            } => !declaration.Type.IsVar,
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                                     assignment.Right == node,
            _ => false
        };
}
