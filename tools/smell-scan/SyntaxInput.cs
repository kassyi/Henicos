using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Parses a file for the detectors. A file with <c>#if</c> directives is parsed once for each set of symbols that is
/// needed to make every <c>#if</c>, <c>#elif</c> and <c>#else</c> branch active at least once, because Roslyn skips the
/// code of an inactive branch. The symbols are read from the conditions of the file itself, so a symbol that the
/// repository adds later needs no change here.
/// </summary>
/// <remarks>
/// With up to <see cref="MaxExhaustiveSymbols"/> symbols the sets are tried by size and then by symbol order, and the
/// search stops as soon as every branch has been active once, so every branch that any assignment can reach is
/// measured. A file with more symbols tries the empty set, each symbol alone, all symbols, and all but one symbol.
/// A set is kept only when it activates a branch that no earlier set did.
/// </remarks>
internal static class SyntaxInput
{
    /// <summary>The largest number of symbols whose every subset is tried.</summary>
    public const int MaxExhaustiveSymbols = 8;

    /// <summary>Parses <paramref name="text"/>, which belongs to <paramref name="path"/>.</summary>
    public static ParsedFile Parse(string path, string text)
    {
        var source = SourceText.From(text);
        var first = ParseWith(path, source, []);
        if (GeneratedCodeFilter.HasGeneratedHeader(first.Root))
        {
            return new ParsedFile(path, true, [], []);
        }

        var branches = BranchDirectives(first.Root);
        if (branches.Count == 0)
        {
            return new ParsedFile(path, false, [first], []);
        }

        var variants = ImmutableArray.CreateBuilder<DetectorContext>();
        variants.Add(first);
        var covered = new HashSet<int>();
        AddTaken(first.Root, covered);
        foreach (var symbols in CandidateSets(ConditionSymbols(first.Root)))
        {
            if (covered.Count >= branches.Count)
            {
                break;
            }

            var variant = ParseWith(path, source, symbols);
            if (AddTaken(variant.Root, covered))
            {
                variants.Add(variant);
            }
        }

        var unmeasured = new List<int>();
        foreach (var position in branches)
        {
            if (!covered.Contains(position))
            {
                unmeasured.Add(source.Lines.GetLineFromPosition(position).LineNumber + 1);
            }
        }

        unmeasured.Sort();
        return new ParsedFile(path, false, variants.ToImmutable(), [.. unmeasured]);
    }

    private static DetectorContext ParseWith(string path, SourceText source, IReadOnlyList<string> symbols)
    {
        var options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(symbols);
        var tree = CSharpSyntaxTree.ParseText(source, options, path);
        return new DetectorContext(path, tree.GetRoot(), source);
    }

    private static List<DirectiveTriviaSyntax> Directives(SyntaxNode root)
    {
        var directives = new List<DirectiveTriviaSyntax>();
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsDirective && trivia.GetStructure() is DirectiveTriviaSyntax directive)
            {
                directives.Add(directive);
            }
        }

        return directives;
    }

    private static HashSet<int> BranchDirectives(SyntaxNode root)
    {
        var positions = new HashSet<int>();
        foreach (var directive in Directives(root))
        {
            if (directive is IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax or ElseDirectiveTriviaSyntax)
            {
                positions.Add(directive.SpanStart);
            }
        }

        return positions;
    }

    private static bool AddTaken(SyntaxNode root, HashSet<int> covered)
    {
        var added = false;
        foreach (var directive in Directives(root))
        {
            var taken = directive switch
            {
                IfDirectiveTriviaSyntax ifDirective => ifDirective.BranchTaken,
                ElifDirectiveTriviaSyntax elifDirective => elifDirective.BranchTaken,
                ElseDirectiveTriviaSyntax elseDirective => elseDirective.BranchTaken,
                _ => false,
            };
            if (taken && covered.Add(directive.SpanStart))
            {
                added = true;
            }
        }

        return added;
    }

    private static List<string> ConditionSymbols(SyntaxNode root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directive in Directives(root))
        {
            var condition = directive switch
            {
                IfDirectiveTriviaSyntax ifDirective => ifDirective.Condition,
                ElifDirectiveTriviaSyntax elifDirective => elifDirective.Condition,
                _ => null,
            };
            if (condition is null)
            {
                continue;
            }

            foreach (var node in condition.DescendantNodesAndSelf())
            {
                if (node is IdentifierNameSyntax name)
                {
                    names.Add(name.Identifier.ValueText);
                }
            }
        }

        var sorted = new List<string>(names);
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static IEnumerable<string[]> CandidateSets(List<string> symbols)
    {
        var count = symbols.Count;
        if (count <= MaxExhaustiveSymbols)
        {
            var masks = new List<int>();
            for (var mask = 1; mask < 1 << count; mask++)
            {
                masks.Add(mask);
            }

            masks.Sort(static (a, b) =>
            {
                var byBits = System.Numerics.BitOperations.PopCount((uint)a)
                    .CompareTo(System.Numerics.BitOperations.PopCount((uint)b));
                return byBits != 0 ? byBits : a.CompareTo(b);
            });
            foreach (var mask in masks)
            {
                yield return Select(symbols, mask);
            }

            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            yield return [symbols[i]];
        }

        yield return [.. symbols];
        for (var skipped = 0; skipped < count && count < 31; skipped++)
        {
            yield return Select(symbols, ((1 << count) - 1) & ~(1 << skipped));
        }
    }

    private static string[] Select(List<string> symbols, int mask)
    {
        var chosen = new List<string>();
        for (var i = 0; i < symbols.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                chosen.Add(symbols[i]);
            }
        }

        return [.. chosen];
    }
}
