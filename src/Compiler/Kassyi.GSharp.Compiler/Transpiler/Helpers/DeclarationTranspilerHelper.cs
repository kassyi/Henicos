namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal static class DeclarationTranspilerHelper
{
    public static void TranspileCombinedDeclarations(
        SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol,
        bool isVar,
        bool isPointer,
        ref TranspileContext context)
    {
        if (isVar)
        {
            context.Emitter.Writer.Append("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.Emitter.Dialect.AppendType(ref context, pointerType.PointedAtType);
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            }
        }
        context.Emitter.Writer.Append(" ");

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Emitter.Writer.Append('*');
            }

            CppNameResolver.AppendDeclaredName(v, v.Identifier, ref context);

            WriteDeclaratorInitializer(v, ref context);

            if (i < variables.Count - 1)
            {
                context.Emitter.Writer.Append(',');
            }
        }
    }

    public static void WriteDeclaratorInitializer(VariableDeclaratorSyntax variable, ref TranspileContext context)
    {
        // An explicit initializer (= default and new() included) is a C# initialization that [SkipLocalsInit] does not
        // remove (Roslyn emits initobj for it); the attribute only affects the zero fill of a stackalloc
        if (variable.Initializer != null)
        {
            context.Emitter.Writer.Append(" = ");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }
}
