using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal static class TerminalTrap
{
    public static void EmitIfUnset(
        ref TranspileContext context,
        string resultVar,
        string flagSuffix,
        string message = "Sequence contains no elements")
    {
        context.Emitter.Writer.AppendLine($"if (!{resultVar}{flagSuffix})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Prelude.Trap}(GSharpErrorCode::InvalidOperation, \"{message}\");");
        }
    }
}
