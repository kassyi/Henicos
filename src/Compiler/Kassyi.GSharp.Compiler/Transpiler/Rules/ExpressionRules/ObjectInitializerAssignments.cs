namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes the member assignments of an object initializer or a <c>with</c> initializer to the temporary that holds the
/// value being built (<c>__gsharp_tmp</c>), a nested member initializer included.
/// </summary>
/// <remarks>
/// A nested member initializer, <c>new Outer { I = { B = 5 } }</c>, assigns the members of the existing <c>I</c> and
/// does not replace it (C# spec §12.8.17.3), so each inner assignment is written on the whole path from the temporary:
/// <c>__gsharp_tmp.I.B = 5;</c> (#1291). The assignments are written in the order of the source, depth first, which is
/// the order C# evaluates them in.
/// </remarks>
internal static class ObjectInitializerAssignments
{
    /// <summary>
    /// Writes <c>__gsharp_tmp.&lt;member&gt; = &lt;value&gt;;</c> for each assignment of <paramref name="initializer"/>,
    /// and the assignments of a nested member initializer on the path to its member; each statement ends with a line
    /// break when <paramref name="lineBreaks"/> is set, otherwise with a space.
    /// </summary>
    public static void Append(InitializerExpressionSyntax initializer, bool lineBreaks, ref TranspileContext context)
    {
        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment)
            {
                continue;
            }

            if (assignment.Right is InitializerExpressionSyntax nested)
            {
                Append(nested, lineBreaks, ref context);
                continue;
            }

            context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.AggregateTemp}.");
            AppendPath(assignment, initializer, ref context);
            ComputedPropertyGetterCall.TranspileAssignment(assignment, " = ", ref context);
            context.Emitter.Writer.Append(";");
            if (lineBreaks)
            {
                context.Emitter.Writer.AppendLine();
            }
            else
            {
                context.Emitter.Writer.Append(' ');
            }
        }
    }

    // <outer member>.<inner member>. ... for the members whose nested initializers hold assignment, outermost first
    private static void AppendPath(AssignmentExpressionSyntax assignment, InitializerExpressionSyntax initializer,
        ref TranspileContext context)
    {
        if (initializer.Parent is not AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax outerInitializer } outer ||
            outer.Right != initializer)
        {
            return;
        }

        AppendPath(outer, outerInitializer, ref context);
        context.Visitor.Visit(outer.Left);
        context.Emitter.Writer.Append('.');
    }
}
