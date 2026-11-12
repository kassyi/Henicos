namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>Finds the names of the <c>__global__</c> functions declared in a hand-written CUDA source.</summary>
/// <remarks>
/// The scan reads C++ tokens, skipping comments, string and character literals and preprocessor lines, so a
/// <c>__global__</c> inside any of them is not a declaration. After <c>__global__</c> it reads the declaration up to
/// the first <c>(</c>: the identifier just before it is the name. Attribute specifiers that carry their own parentheses
/// (<c>__launch_bounds__(256, 2)</c>, <c>__maxnreg__(32)</c>, <c>__cluster_dims__(2, 1, 1)</c>,
/// <c>__attribute__((used))</c>, <c>__declspec(noinline)</c>, <c>[[nodiscard]]</c>) are skipped wherever they stand,
/// and plain qualifiers (<c>void</c>, <c>__noinline__</c>, <c>static</c>) are passed over. A declaration with no
/// identifier before its <c>(</c>, or that ends first (<c>;</c>, <c>{</c>, a template argument list), yields no name.
/// </remarks>
internal static class CudaKernelNameScanner
{
    public static ImmutableArray<string> Scan(string source)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var lexer = new CudaLexer(source);
        while (lexer.Next())
        {
            if (lexer.IsIdentifier("__global__") && TryReadName(ref lexer, out var name))
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryReadName(ref CudaLexer lexer, out string name)
    {
        name = "";
        var candidate = ReadOnlySpan<char>.Empty;
        while (lexer.Next())
        {
            if (lexer.Kind == CudaTokenKind.Identifier && !IsParenthesizedAttribute(lexer.Text))
            {
                candidate = lexer.Text;
                continue;
            }

            // A qualified name (ns::Kernel): the identifier after "::" becomes the candidate.
            var skipped = lexer.Kind == CudaTokenKind.Identifier
                ? SkipArgumentList(ref lexer)
                : lexer.IsPunctuation('[')
                    ? SkipStandardAttribute(ref lexer)
                    : lexer.IsPunctuation(':');
            if (skipped)
            {
                continue;
            }

            if (lexer.IsPunctuation('(') && !candidate.IsEmpty && candidate is not "void")
            {
                name = candidate.ToString();
                return true;
            }

            return false;
        }

        return false;
    }

    // <attribute>(...): the current token is the attribute name.
    private static bool SkipArgumentList(ref CudaLexer lexer) =>
        lexer.Next() && lexer.IsPunctuation('(') && lexer.SkipBalanced('(', ')');

    // [[...]]: the current token is the first '['; the second opens a balanced list that ends with "]]".
    private static bool SkipStandardAttribute(ref CudaLexer lexer) =>
        lexer.Next() && lexer.IsPunctuation('[') && lexer.SkipBalanced('[', ']') && lexer.Next() &&
        lexer.IsPunctuation(']');

    private static bool IsParenthesizedAttribute(ReadOnlySpan<char> identifier) =>
        identifier is "__launch_bounds__" or "__maxnreg__" or "__cluster_dims__" or "__attribute__" or "__declspec"
            or "alignas";
}
