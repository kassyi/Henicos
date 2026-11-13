using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>
/// Terminal strategy for <c>Max</c> and <c>Min</c>.
/// </summary>
/// <remarks>
/// A <c>float</c> or <c>double</c> sequence places NaN as System.Linq does, whatever its position: <c>Max</c> skips
/// NaN and gives NaN only when every element is NaN; <c>Min</c> gives the first NaN and reads no element after it.
/// </remarks>
internal sealed class ExtremaStrategy(bool isMax) : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        context.Emitter.Writer.AppendLine($"bool {resultVar}{InitSuffix} = false;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var isFloating = stage.Symbol.ReturnType.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        var value = $"{ValuePrefix}{valIndex}";
        if (isMax)
        {
            EmitMax(context.Emitter.Writer, resultVar, value, isFloating);
        }
        else
        {
            EmitMin(context.Emitter.Writer, resultVar, value, isFloating);
        }
    }

    // if (!<res>_init[ || <is_nan>(<res>)]) { <res> = <v>; <res>_init = true; }
    // else { if (<v> > <res>) { <res> = <v>; } }
    private static void EmitMax(SourceWriter writer, string resultVar, string value, bool isFloating)
    {
        if (isFloating)
        {
            writer.AppendLine($"if (!{resultVar}{InitSuffix} || {LinqIsNaN}({resultVar}))");
        }
        else
        {
            writer.AppendLine($"if (!{resultVar}{InitSuffix})");
        }
        using (writer.Braces())
        {
            writer.AppendLine($"{resultVar} = {value};");
            writer.AppendLine($"{resultVar}{InitSuffix} = true;");
        }
        writer.AppendLine("else");
        using (writer.Braces())
        {
            writer.AppendLine($"if ({value} > {resultVar})");
            using (writer.Braces())
            {
                writer.AppendLine($"{resultVar} = {value};");
            }
        }
    }

    // if (!<res>_init) { <res> = <v>; <res>_init = true;[ if (<is_nan>(<res>)) { break; }] }
    // else { if (<v> < <res>) { <res> = <v>; }[ else if (<is_nan>(<v>)) { <res> = <v>; break; }] }
    private static void EmitMin(SourceWriter writer, string resultVar, string value, bool isFloating)
    {
        writer.AppendLine($"if (!{resultVar}{InitSuffix})");
        using (writer.Braces())
        {
            writer.AppendLine($"{resultVar} = {value};");
            writer.AppendLine($"{resultVar}{InitSuffix} = true;");
            if (isFloating)
            {
                writer.AppendLine($"if ({LinqIsNaN}({resultVar})) {{ break; }}");
            }
        }
        writer.AppendLine("else");
        using (writer.Braces())
        {
            writer.AppendLine($"if ({value} < {resultVar})");
            using (writer.Braces())
            {
                writer.AppendLine($"{resultVar} = {value};");
            }
            if (isFloating)
            {
                writer.AppendLine($"else if ({LinqIsNaN}({value})) {{ {resultVar} = {value}; break; }}");
            }
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) =>
        TerminalTrap.EmitIfUnset(ref context, resultVar, InitSuffix);
}
