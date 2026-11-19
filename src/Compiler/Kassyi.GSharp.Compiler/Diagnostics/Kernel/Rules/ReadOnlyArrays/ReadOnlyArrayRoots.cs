namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// Which expressions hold a <c>[GpuReadOnly]</c> array: the array itself (a parameter, a field or a property with the
/// attribute, or the parameter of a callee that is being checked), and what is computed from it without leaving the
/// array: a conversion, <c>Gpu.NotNull</c>, a slice or <c>AsSpan()</c>, a <c>new Span&lt;T&gt;(array)</c>, a
/// conditional of such values, and a variable (a local, a parameter or a field of a struct local) that is given nothing
/// else.
/// </summary>
/// <param name="model">The semantic model of the body.</param>
/// <param name="checkedParameter">
/// The parameter of the callee that is being checked, taken as a read-only array whatever its attributes are, or
/// <see langword="null"/> for the method that is analysed.
/// </param>
/// <param name="followsAttributes">
/// Whether a <c>[GpuReadOnly]</c> parameter, field or property is a read-only array. A callee is checked for the one
/// parameter, since a write to another <c>[GpuReadOnly]</c> parameter is reported where it is written.
/// </param>
/// <param name="sources">What each variable of the body is given (<see cref="AliasSources"/>).</param>
internal sealed class ReadOnlyArrayRoots(
    SemanticModel model,
    IParameterSymbol? checkedParameter,
    bool followsAttributes,
    AliasSources sources)
{
    // The variables whose every source is a read-only array, as far as the fixed point has got
    private readonly HashSet<AliasKey> _readOnlyVariables = new();

    /// <summary>
    /// Solves which variables hold only read-only arrays; call once before <see cref="RootOf(ExpressionSyntax)"/>.
    /// </summary>
    public void Solve()
    {
        foreach (var key in sources.Entries.Keys)
        {
            // A parameter (or a field of one), or a slot given a value that is not read, also holds what its
            // declaration says; unless that is a read-only array, the body assignments are not its only values.
            if (!HoldsUnreadValue(key) || RootOfSymbol(key.Last) != null)
            {
                _readOnlyVariables.Add(key);
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var pair in sources.Entries)
            {
                if (_readOnlyVariables.Contains(pair.Key) &&
                    !pair.Value.AsStackEnumerable().All(this, static (source, self) => self.RootOf(source, 0) != null))
                {
                    _readOnlyVariables.Remove(pair.Key);
                    changed = true;
                }
            }
        } while (changed);
    }

    /// <summary>
    /// The read-only array that <paramref name="expression"/> holds, or <see langword="null"/> when it holds anything
    /// else or when that cannot be told.
    /// </summary>
    public ISymbol? RootOf(ExpressionSyntax expression) => RootOf(expression, 0);

    /// <summary>
    /// The receiver of a call written <c>x.M(...)</c> when <paramref name="method"/> is an instance or extension
    /// method, else its first argument (<c>Array.Fill(x, ...)</c>, <c>MemoryExtensions.AsSpan(x)</c>).
    /// </summary>
    private static ExpressionSyntax? ReceiverOrFirstArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method) =>
        invocation.Expression is MemberAccessExpressionSyntax access && (!method.IsStatic || method.ReducedFrom != null)
            ? access.Expression
            : invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : null;

    private ISymbol? RootOf(ExpressionSyntax expression, int depth)
    {
        // Variables that hold each other in a cycle make the walk end here
        if (depth > 64)
        {
            return null;
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return RootOf(parenthesized.Expression, depth + 1);
            case CastExpressionSyntax cast:
                return RootOf(cast.Expression, depth + 1);
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang:
                return RootOf(bang.Operand, depth + 1);
            case RefExpressionSyntax reference:
                return RootOf(reference.Expression, depth + 1);
            case ConditionalExpressionSyntax conditional:
                return RootOf(conditional.WhenTrue, depth + 1) is { } root &&
                       RootOf(conditional.WhenFalse, depth + 1) != null
                    ? root
                    : null;
            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                return RootOfVariable(expression, depth);
            case InvocationExpressionSyntax invocation:
                return RootOfCall(invocation, depth);
            case ObjectCreationExpressionSyntax { ArgumentList.Arguments: [{ } only] } creation when
                model.GetTypeInfo(creation).Type.TryGetArrayLikeElementType(out _):
                return RootOf(only.Expression, depth + 1);
            default:
                return null;
        }
    }

    private ISymbol? RootOf(AliasSource source, int depth) =>
        source.Expression is { } expression
            ? RootOf(expression, depth)
            : source.Key is { } key
                ? RootOfKey(key, depth)
                : null;

    // A variable that is given values is read-only when all of them are; a parameter (or a field of one) or a slot
    // given an unread value falls back to its declaration attribute.
    private ISymbol? RootOfVariable(ExpressionSyntax expression, int depth) =>
        AliasKey.Of(expression, model) is { } key
            ? RootOfKey(key, depth)
            : RootOfSymbol(model.GetSymbol(expression));

    private ISymbol? RootOfKey(AliasKey key, int depth)
    {
        if (depth > 64)
        {
            return null;
        }

        // The walk ends at the depth limit when the sources form a cycle (a swap, var t = a; a = t;) or a chain longer
        // than the limit; the fixed point of Solve has already proved every source read-only, so the slot stands in
        if (sources.Entries.TryGetValue(key, out var given))
        {
            return !_readOnlyVariables.Contains(key)
                ? null
                : given.Length > 0
                    ? RootOf(given[0], depth + 1) ?? key.Last
                    : RootOfSymbol(key.Last);
        }

        // A local that is given nothing holds null (no array); a slot given an unread value holds what it declares
        return HoldsUnreadValue(key) ? RootOfSymbol(key.Last) : null;
    }

    private bool HoldsUnreadValue(AliasKey key) =>
        key.Variable is IParameterSymbol || sources.HoldsUnreadValue(key);

    private ISymbol? RootOfSymbol(ISymbol? symbol)
    {
        switch (symbol)
        {
            case IParameterSymbol parameter when SymbolEqualityComparer.Default.Equals(parameter, checkedParameter):
                return parameter;
            case IParameterSymbol parameter when IsReadOnlyArray(parameter, parameter.Type):
                return parameter;
            case IFieldSymbol { IsStatic: false } field when IsReadOnlyArray(field, field.Type):
                return field;
            case IPropertySymbol { IsStatic: false } property when IsReadOnlyArray(property, property.Type):
                return property;
            default:
                return null;
        }
    }

    private bool IsReadOnlyArray(ISymbol symbol, ITypeSymbol type) =>
        followsAttributes && symbol.HasAttribute(KnownAttributes.GpuReadOnly) && type.TryGetArrayLikeElementType(out _);

    private ISymbol? RootOfCall(InvocationExpressionSyntax invocation, int depth)
    {
        if (model.GetSymbol(invocation) is not IMethodSymbol method)
        {
            return null;
        }

        // Gpu.NotNull(array) returns its argument
        if (GpuMacroClassifier.Classify(method).Kind == GpuMacroKind.NotNull &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            return RootOf(invocation.ArgumentList.Arguments[0].Expression, depth + 1);
        }

        // A slice or AsSpan() views the same elements
        if (!SpanWriteClassifier.IsPureViewMethod(method))
        {
            return null;
        }

        var operand = ReceiverOrFirstArgument(invocation, method);
        return operand == null ? null : RootOf(operand, depth + 1);
    }
}
