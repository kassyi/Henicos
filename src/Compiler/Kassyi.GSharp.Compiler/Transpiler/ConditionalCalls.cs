namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Whether C# removes a call because of <c>[Conditional]</c>: the method (for an override, the method it overrides
/// first) has <c>[Conditional]</c> attributes and none of their symbols is defined at the call.
/// </summary>
/// <remarks>
/// [WHY] The rule of the C# specification (§23.5.3.2) and of Roslyn's <c>MethodSymbol.IsConditional</c> /
/// <c>CallsAreOmitted</c>: the symbols are those of the parse options of the call's tree, changed by the
/// <c>#define</c> and <c>#undef</c> directives of the file. Every output asks <see cref="RemovedCalls"/>, which also
/// covers the partial methods without an implementation.
/// </remarks>
internal static class ConditionalCalls
{
    // System.Diagnostics.ConditionalAttribute
    private const string ConditionalAttributeName = "ConditionalAttribute";

    /// <summary>Whether C# removes a call of <paramref name="method"/> written in <paramref name="tree"/>.</summary>
    public static bool IsRemoved(IMethodSymbol method, SyntaxTree tree)
    {
        var conditions = GetConditions(method);
        if (conditions.IsEmpty)
        {
            return false;
        }

        foreach (var symbol in conditions)
        {
            if (IsDefinedIn(symbol, tree))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The symbols of the <c>[Conditional]</c> attributes of <paramref name="method"/> (for an override, of the method
    /// it overrides first); empty when calls of it are never removed for a condition.
    /// </summary>
    public static ImmutableArray<string> GetConditions(IMethodSymbol method)
    {
        var declared = method;
        while (declared.OverriddenMethod is { } overridden)
        {
            declared = overridden;
        }

        var conditions = ImmutableArray<string>.Empty;
        foreach (var attribute in declared.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: ConditionalAttributeName, ContainingType: null } attributeClass &&
                attributeClass.ContainingNamespace.IsSystemDiagnostics() &&
                attribute.ConstructorArguments is [{ Value: string symbol }])
            {
                conditions = conditions.Add(symbol);
            }
        }

        return conditions;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is defined in <paramref name="tree"/>: by its parse options, changed by the
    /// <c>#define</c> and <c>#undef</c> directives of the file.
    /// </summary>
    public static bool IsDefinedIn(string symbol, SyntaxTree tree)
    {
        var defined = IsDefinedByOptions(symbol, tree);

        // [WHY] C# allows #define and #undef only before the first token of the file, so the leading trivia of the
        // root holds all of them, and all come before the call; one in an inactive #if branch does not count
        foreach (var trivia in tree.GetRoot().GetLeadingTrivia())
        {
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define when define.Name.ValueText == symbol:
                    defined = true;
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undefine when undefine.Name.ValueText == symbol:
                    defined = false;
                    break;
            }
        }

        return defined;
    }

    /// <summary>
    /// Whether the parse options of <paramref name="tree"/> define <paramref name="symbol"/>, before the directives of
    /// the file; a generated file of the same compilation has the same options.
    /// </summary>
    public static bool IsDefinedByOptions(string symbol, SyntaxTree tree)
    {
        if (tree.Options is not CSharpParseOptions options)
        {
            return false;
        }

        // [WHY] A loop: the names are a lazy IEnumerable<string>, which the embedded StackLinq does not take
        foreach (var name in options.PreprocessorSymbolNames)
        {
            if (name == symbol)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a <c>#define</c> directive of <paramref name="tree"/> is active.</summary>
    public static bool HasDefineDirective(SyntaxTree tree)
    {
        foreach (var trivia in tree.GetRoot().GetLeadingTrivia())
        {
            if (trivia.GetStructure() is DefineDirectiveTriviaSyntax { IsActive: true })
            {
                return true;
            }
        }

        return false;
    }
}
