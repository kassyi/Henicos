namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Whether C# removes a call because of <c>[Conditional]</c>: the method (for an override, the method it overrides
/// first) has <c>[Conditional]</c> attributes and none of their symbols is defined at the call.
/// </summary>
/// <remarks>
/// [WHY] The rule of the C# specification (§23.5.3.2) and of Roslyn's <c>MethodSymbol.IsConditional</c> /
/// <c>CallsAreOmitted</c>: the symbols are those of the parse options of the call's tree, changed by the
/// <c>#define</c> and <c>#undef</c> directives of the file.
/// </remarks>
internal static class ConditionalCalls
{
    private const string ConditionalAttributeName = "System.Diagnostics.ConditionalAttribute";

    /// <summary>Whether C# removes <paramref name="call"/> of <paramref name="method"/>.</summary>
    public static bool IsRemoved(IMethodSymbol method, SyntaxNode call)
    {
        var declared = method;
        while (declared.OverriddenMethod is { } overridden)
        {
            declared = overridden;
        }

        var hasCondition = false;
        foreach (var attribute in declared.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != ConditionalAttributeName ||
                attribute.ConstructorArguments is not [{ Value: string symbol }])
            {
                continue;
            }

            hasCondition = true;
            if (IsDefinedAt(symbol, call))
            {
                return false;
            }
        }

        return hasCondition;
    }

    private static bool IsDefinedAt(string symbol, SyntaxNode call)
    {
        var defined = false;
        if (call.SyntaxTree.Options is CSharpParseOptions options)
        {
            // [WHY] A loop: the names are a lazy IEnumerable<string>, which the embedded StackLinq does not take
            foreach (var name in options.PreprocessorSymbolNames)
            {
                defined |= name == symbol;
            }
        }

        // [WHY] C# allows #define and #undef only before the first token of the file, so the leading trivia of the
        // root holds all of them, and all come before the call
        foreach (var trivia in call.SyntaxTree.GetRoot().GetLeadingTrivia())
        {
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax define when define.Name.ValueText == symbol:
                    defined = true;
                    break;
                case UndefDirectiveTriviaSyntax undefine when undefine.Name.ValueText == symbol:
                    defined = false;
                    break;
            }
        }

        return defined;
    }
}
