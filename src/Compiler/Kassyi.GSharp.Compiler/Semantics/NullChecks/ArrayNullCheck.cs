using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// The null check of a C# array read (#706): <c>Length</c> and the other <c>System.Array</c> members, an element and
/// <c>foreach</c> throw <see cref="NullReferenceException"/> on a null array in C#, and so do <c>Length</c>,
/// <c>IsEmpty</c>, an element and <c>foreach</c> of a default <c>ImmutableArray&lt;T&gt;</c>, whose array is null. The
/// receiver goes through the Prelude helper <c>__gsharp_array_ref(&lt;receiver&gt;, &lt;site&gt;)</c>, which fails on
/// every build (#1409). In a kernel body the place is a site of the kernel's error site table, so the host exception
/// carries it on CUDA as well (<see cref="NullCheckSite"/>).
/// </summary>
/// <remarks>
/// <para>
/// Only a receiver of a C# array type or of <c>ImmutableArray&lt;T&gt;</c> is checked: a span is never null in C#, and
/// <c>is null</c>, <c>??</c>, a conversion to a span (<c>AsSpan</c>) or passing the array along read nothing.
/// </para>
/// <para>
/// No check is emitted where the receiver cannot be null, as RyuJIT drops a null check that an assertion proves
/// (symbol-based only, <see cref="NonNullProof"/>): a stack array local, a non-nullable array parameter of a device
/// function whose calls check it, and a conditional whose branches are both such receivers. A kernel parameter is
/// checked whatever its annotation: the launcher passes a null array as a C# call does.
/// </para>
/// </remarks>
internal static class ArrayNullCheck
{
    /// <summary>The text of the error of a registered site, whose place the error site table holds.</summary>
    public const string SiteMessage = "G#: a null array is read";

    /// <summary>The text of the error, followed by the place of the receiver (<see cref="SourcePlace"/>).</summary>
    public const string MessagePrefix = SiteMessage + NullCheckSiteWriter.PlaceSeparator;

    /// <summary>The trap kind of the site ID, which tells the site apart from another trap at the same place.</summary>
    internal const string TrapKind = "ArrayNull";

    /// <summary>Whether a read through <paramref name="receiver"/> needs the null check.</summary>
    public static bool NeedsCheck(ExpressionSyntax receiver, SemanticModel semanticModel) =>
        IsCheckedType(semanticModel.GetTypeSymbol(receiver)) && MayBeNull(receiver, semanticModel);

    // A C# array, or an ImmutableArray<T>, whose default holds a null array
    internal static bool IsCheckedType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol || IsImmutableArray(type);

    private static bool IsImmutableArray(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Name: "ImmutableArray" } named &&
        named.ContainingNamespace.IsSystemCollectionsImmutable();

    private static bool MayBeNull(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        expression = SkipParentheses(expression);

        // c ? a : b is null only when the branch it takes is (a null literal branch may be)
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return MayBeNull(conditional.WhenTrue, semanticModel) || MayBeNull(conditional.WhenFalse, semanticModel);
        }

        // A stack array (#1543), or a non-nullable array that a call has checked (#1409)
        if (NonNullProof.IsProvenNonNull(expression, semanticModel))
        {
            return false;
        }

        // A host static member, which GSH0065 reports
        return !IsHostStaticField(semanticModel.GetSymbol(expression));
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is a static field (a host static member, which GSH0065 reports).
    /// </summary>
    internal static bool IsHostStaticField(ISymbol? symbol) => symbol is IFieldSymbol { IsStatic: true };

    /// <summary>
    /// Whether <paramref name="node"/> is an element of a one-dimensional C# array at an <c>int</c>-like index (not
    /// <c>^i</c> or a range): a location whose null and bounds check C# makes when it reads or stores the element.
    /// </summary>
    public static bool IsArrayElement(ElementAccessExpressionSyntax node, SemanticModel model) =>
        node.ArgumentList.Arguments is [{ RefKindKeyword.RawKind: (int)SyntaxKind.None } argument] &&
        model.GetTypeSymbol(node.Expression) is IArrayTypeSymbol { Rank: 1 } &&
        !IsIndexOrRange(argument.Expression, model);

    /// <summary>Plans the error site of an array read (element or Length) from a report location.</summary>
    internal static NullCheckSite PlanReadSite(Location location, Compilation compilation, ErrorSiteTable? table,
        string? currentMemberName) =>
        NullCheckSiteWriter.PlanSite(location, null, SiteMessage, TrapKind, compilation, table, currentMemberName);

    // Whether type is System.Index or System.Range
    internal static bool IsIndexOrRangeType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Index" or "Range" } named &&
        named.ContainingNamespace.IsSystemNamespace();

    // a[^1] and a[1..]: an argument of type System.Index or System.Range
    internal static bool IsIndexOrRange(ExpressionSyntax argument, SemanticModel model) =>
        argument is RangeExpressionSyntax || IsIndexOrRangeType(model.GetTypeSymbol(argument));

    // An instance member of System.Array: Length, LongLength, Rank, GetLength, ... (not an extension method such as
    // AsSpan), or Length / IsEmpty of ImmutableArray<T>, which read its array (IsDefault and AsSpan do not)
    internal static bool IsArrayMember(ISymbol? member) =>
        member is { IsStatic: false, ContainingType.SpecialType: SpecialType.System_Array } &&
        member is not IMethodSymbol { MethodKind: MethodKind.ReducedExtension } ||
        member is IPropertySymbol { IsStatic: false, Name: "Length" or "IsEmpty" } property &&
        IsImmutableArray(property.ContainingType);

    // Parentheses, and the null-forgiving operator, which has no run-time meaning (a! is null when a is)
    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppressed.Operand;
                    break;
                default:
                    return expression;
            }
        }
    }
}
