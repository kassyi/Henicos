#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>How a temporary of a sequence is declared: a reference, a deduced type or a named type.</summary>
internal enum IrSequenceDeclarationForm
{
    Ref,
    Deduced,
    Named
}

/// <summary>
/// The temporaries of the sequences of one method, as the syntax path writes them (from the C++ text) or as the IR
/// holds them: the first temporary of each sequence in the order they are spelled, and every declaration by number.
/// </summary>
/// <param name="Firsts">The number of the first temporary of each sequence, in the order the sequences appear.</param>
/// <param name="Declarations">Each declared temporary by its number, with how it is declared.</param>
/// <param name="OtherLambdas">The immediately invoked lambdas that do not begin with a temporary.</param>
internal sealed record IrSequenceTemporaries(
    ImmutableArray<int> Firsts,
    ImmutableArray<(int Number, IrSequenceDeclarationForm Form)> Declarations,
    int OtherLambdas)
{
    private const string Lambda = "([&]()";
    private const string Temp = "__gsharp_seq_";

    /// <summary>
    /// The temporaries of the sequences in <paramref name="method"/>, numbered as a writer numbers them.
    /// </summary>
    public static IrSequenceTemporaries Of(IrMethod method)
    {
        var numbered = IrSequenceNumbering.Of(method.Body);
        var firsts = IrNodeChildren.DescendantsAndSelf(method.Body)
            .OfType<IrSequence>()
            .Select(sequence => numbered.IndexOf(sequence.Locals[0]))
            .ToImmutableArray();
        var declarations = numbered
            .Select(static (variable, number) => (number, variable.RefKind != RefKind.None
                ? IrSequenceDeclarationForm.Ref
                : variable.DeclaredTypeForm == IrDeclaredTypeForm.Deduced
                    ? IrSequenceDeclarationForm.Deduced
                    : IrSequenceDeclarationForm.Named))
            .ToImmutableArray();
        return new IrSequenceTemporaries(firsts, declarations, 0);
    }

    /// <summary>
    /// The temporaries the syntax path declares in its native C++ (the dialect the decisions of the IR are made for)
    /// for <paramref name="declaration"/>, a device function: those of the function itself, without the struct
    /// definitions and constructors written in front of it, which are members of their own.
    /// </summary>
    public static IrSequenceTemporaries OfSyntaxPath(MethodDeclarationSyntax declaration, SemanticModel model)
    {
        var structs = new StructSourceCollector();
        var text = MethodTranspiler.GenerateCudaSource(MethodEmissionPlan.Create(declaration, model, isKernel: false),
            null, HostDialects.Cpu, out _, structs);
        foreach (var part in structs.Definitions.Concat(structs.Disposes))
        {
#pragma warning disable RS0030 // C++ text: the struct parts written in front of the function are not its own
            text = text.Replace(part.Text, string.Empty);
#pragma warning restore RS0030
        }

        return Of(text);
    }

    /// <summary>The temporaries that the C++ text <paramref name="text"/> declares.</summary>
    public static IrSequenceTemporaries Of(string text)
    {
        var firsts = ImmutableArray.CreateBuilder<int>();
        var others = 0;
        for (var at = text.IndexOf(Lambda, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(Lambda, at + Lambda.Length, StringComparison.Ordinal))
        {
            var open = text.IndexOf('{', at);
            var statement = open < 0 ? -1 : SkipSpaces(text, open + 1);
            if (statement >= 0 && Declaration(text, statement) is { } first)
            {
                firsts.Add(first.Number);
            }
            else
            {
                others++;
            }
        }

        var declarations = ImmutableArray.CreateBuilder<(int Number, IrSequenceDeclarationForm Form)>();
        for (var at = text.IndexOf(Temp, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(Temp, at + Temp.Length, StringComparison.Ordinal))
        {
            var start = StatementStart(text, at);
            if (text.IndexOf(Temp, start, StringComparison.Ordinal) == at && Declaration(text, start) is { } declaration)
            {
                declarations.Add(declaration);
            }
        }

        return new IrSequenceTemporaries(firsts.ToImmutable(),
            declarations.OrderBy(static d => d.Number).ToImmutableArray(), others);
    }

    /// <summary>The sequences in a printable form.</summary>
    public override string ToString() =>
        $"A [{string.Join(",", Firsts)}] N {Declarations.Length} D [{string.Join(",",
            Declarations.Select(static d => $"({d.Number},{d.Form})"))}]";

    /// <summary>Whether this holds the same sequences as <paramref name="other"/>.</summary>
    public bool SameAs(IrSequenceTemporaries other) =>
        Firsts.SequenceEqual(other.Firsts) && Declarations.SequenceEqual(other.Declarations);

    // "<type> __gsharp_seq_<n> = " starting at <start>: the number and the form, or null for another statement
    private static (int Number, IrSequenceDeclarationForm Form)? Declaration(string text, int start)
    {
        var name = text.IndexOf(Temp, start, StringComparison.Ordinal);
        if (name <= start || text.IndexOfAny([';', '{', '}', '(', ')', '='], start, name - start) >= 0)
        {
            return null;
        }

        var end = name + Temp.Length;
        var number = 0;
        var digits = 0;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            number = (number * 10) + (text[end] - '0');
            end++;
            digits++;
        }

        if (digits == 0 || string.CompareOrdinal(text, end, " = ", 0, 3) != 0)
        {
            return null;
        }

        var type = text.AsSpan(start, name - start);
        while (type.Length > 0 && char.IsWhiteSpace(type[^1]))
        {
            type = type[..^1];
        }

        if (type is "return" or "")
        {
            return null;
        }

        var form = type is "auto&&"
            ? IrSequenceDeclarationForm.Ref
            : type is "auto"
                ? IrSequenceDeclarationForm.Deduced
                : IrSequenceDeclarationForm.Named;
        return (number, form);
    }

    // The first character of the statement that holds <at>: after the last ';', '{' or '}' before it
    private static int StatementStart(string text, int at)
    {
        var start = at;
        while (start > 0 && text[start - 1] is not (';' or '{' or '}' or '\n'))
        {
            start--;
        }

        return SkipSpaces(text, start);
    }

    private static int SkipSpaces(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        return at;
    }
}
