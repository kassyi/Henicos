namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Writes the C++ literal of a constant field.</summary>
internal static class CppLiteralWriter
{
    public static ref readonly SourceWriter AppendConstantField(this in SourceWriter writer, IFieldSymbol field) =>
        ref writer.Append(ConstexprLiteral.FormatCppConstant(field.Type, field.ConstantValue));
}
