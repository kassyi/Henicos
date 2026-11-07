using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Writes a signed expression tree as text for an overflow report: operands are named <c>x0</c>, <c>x1</c>, ... in the
/// order they first appear, inner operations are parenthesized, and the overflowing operation is put in brackets.
/// </summary>
internal static class SignedExpressionText
{
    /// <summary>Writes <paramref name="node"/> alone.</summary>
    /// <param name="node">The operation.</param>
    /// <returns>The text, such as <c>x0 + x1</c>.</returns>
    public static string Describe(IGpuBigIntExpression node)
    {
        var builder = new StringBuilder();
        Append(builder, node, null, new Dictionary<IGpuBigIntExpression, string>(ReferenceEqualityComparer.Instance),
            isRoot: true);
        return builder.ToString();
    }

    /// <summary>Writes <paramref name="node"/> and the whole expression with <paramref name="node"/> marked.</summary>
    /// <param name="root">The evaluated expression.</param>
    /// <param name="node">The overflowing operation inside <paramref name="root"/>.</param>
    /// <returns>The operation (<c>x0 + x1</c>) and the expression (<c>[x0 + x1] - x2</c>), with the same operand names.</returns>
    public static (string Subexpression, string Expression) Describe(IGpuBigIntExpression root,
        IGpuBigIntExpression node)
    {
        var names = new Dictionary<IGpuBigIntExpression, string>(ReferenceEqualityComparer.Instance);
        var expression = new StringBuilder();
        Append(expression, root, node, names, isRoot: true);
        var subexpression = new StringBuilder();
        Append(subexpression, node, null, names, isRoot: true);
        return (subexpression.ToString(), expression.ToString());
    }

    private static void Append(StringBuilder builder, IGpuBigIntExpression node, IGpuBigIntExpression? marked,
        Dictionary<IGpuBigIntExpression, string> names, bool isRoot)
    {
        var mark = ReferenceEquals(node, marked);
        switch (node)
        {
            case SignedMaxExpression or SignedMinExpression:
                var binaryCall = (SignedBinaryExpression)node;
                builder.Append(mark ? "[" : "").Append(node is SignedMaxExpression ? "Max(" : "Min(");
                Append(builder, binaryCall.Left, marked, names, isRoot: true);
                builder.Append(", ");
                Append(builder, binaryCall.Right, marked, names, isRoot: true);
                builder.Append(mark ? ")]" : ")");
                break;
            case SignedBinaryExpression binary:
                builder.Append(mark ? "[" : isRoot ? "" : "(");
                Append(builder, binary.Left, marked, names, isRoot: false);
                builder.Append(' ').Append(OperatorOf(binary)).Append(' ');
                Append(builder, binary.Right, marked, names, isRoot: false);
                builder.Append(mark ? "]" : isRoot ? "" : ")");
                break;
            case SignedNegateExpression negate:
                AppendPrefix(builder, "-", negate.Target, mark, marked, names);
                break;
            case SignedBitwiseNotExpression not:
                AppendPrefix(builder, "~", not.Target, mark, marked, names);
                break;
            default:
                if (!names.TryGetValue(node, out var name))
                {
                    name = "x" + names.Count.ToString(CultureInfo.InvariantCulture);
                    names.Add(node, name);
                }

                builder.Append(mark ? "[" : "").Append(name).Append(mark ? "]" : "");
                break;
        }
    }

    private static void AppendPrefix(StringBuilder builder, string prefix, IGpuBigIntExpression target, bool mark,
        IGpuBigIntExpression? marked, Dictionary<IGpuBigIntExpression, string> names)
    {
        builder.Append(mark ? "[" : "").Append(prefix);
        Append(builder, target, marked, names, isRoot: false);
        builder.Append(mark ? "]" : "");
    }

    private static string OperatorOf(SignedBinaryExpression binary) => binary switch
    {
        SignedAddExpression => "+",
        SignedSubExpression => "-",
        SignedMulExpression => "*",
        SignedDivExpression => "/",
        SignedModExpression => "%",
        _ => "?"
    };
}
