namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes <c>foreach</c> over a constant string (<c>foreach (char c in Text)</c> of a <c>const string</c>, a literal,
/// <c>nameof(T)</c>) as a counted loop over its UTF-16 code units, as C# reads them. A C++ range-for over the string
/// would read a narrow literal as UTF-8 bytes plus its terminating zero, and a constant has no other device form.
/// </summary>
/// <remarks>
/// <c>foreach</c> over a constant that is <see langword="null"/> throws <see cref="NullReferenceException"/> in C#,
/// which is known at compile time: a failure (GSH0003), as <see cref="ConstantStringElement"/> reports for an element.
/// </remarks>
internal static class ConstantStringForEach
{
    /// <summary>The GSH0003 text for <c>foreach</c> over a constant string that is <see langword="null"/>.</summary>
    public const string NullConstant = "foreach over a null constant string (it throws NullReferenceException)";

    /// <summary>Writes <paramref name="node"/> when its source is a constant string.</summary>
    /// <returns>Whether the source is a constant string and the loop was written (or reported).</returns>
    public static bool TryTranspile(ForEachStatementSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (model.GetTypeSymbol(node.Expression) is not { SpecialType: SpecialType.System_String } ||
            model.GetSafeConstantValue(node.Expression) is not { HasValue: true } constant)
        {
            return false;
        }

        if (constant.Value is not string text)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node.Expression, NullConstant);
            return true;
        }

        var writer = context.Emitter.Writer;
        var index = GSharpInternalNames.Lowering.ForEachStringIndexPrefix + context.Emitter.ForEachElementCounter++;
        // for(int <index> = 0; <index> < <length>; <index>++) { <T> <name> = __gsharp_string_char(u"<text>", <index>);
        // <body> }
        writer.AppendLine($"for(int {index} = 0; {index} < {text.Length}; {index}++)");
        using (writer.Scope())
        {
            if (node.Type.IsVar)
            {
                writer.Append("auto ");
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, model.GetTypeSymbol(node.Type));
                writer.Append(' ');
            }

            CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
            writer.Append(" = ").Append(GSharpInternalNames.Prelude.StringChar);
            using (writer.Parentheses())
            {
                CppStringLiteral.AppendUtf16(writer, text);
                writer.Append($",{index}");
            }

            writer.AppendLine(";");
            context.Visitor.VisitStatementOrBlock(node.Statement);
        }

        return true;
    }
}
