namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The call that a failed check writes. On CUDA, inside the scope of a <c>using</c>, the thread ends with <c>exit</c>,
/// which runs no destructor, so the call passes the guards of the scopes the check leaves to
/// <c>__gsharp_trap_unwind</c>, which records the failure, disposes them (the innermost first, as C# runs the
/// <c>finally</c> blocks while the exception leaves) and ends the thread. Anywhere else it is the plain trap call.
/// </summary>
internal static class UnwindingTrap
{
    /// <summary>
    /// Appends <c>&lt;trap&gt;(GSharpErrorCode::&lt;code&gt;, &lt;site&gt;u)</c> (or the message as a string literal), or
    /// <c>__gsharp_trap_unwind(GSharpErrorCode::&lt;code&gt;, &lt;site&gt;u, &lt;guards&gt;)</c>.
    /// </summary>
    /// <param name="context">The context whose writer receives the call, and whose guards are in scope.</param>
    /// <param name="trapName">The plain trap function of the place (<c>__gsharp_trap</c>, <c>__gsharp_trap_abort</c>).</param>
    /// <param name="errorCode">The <c>GSharpErrorCode</c> member.</param>
    /// <param name="siteId">The site of the kernel's error site table, or 0 when <paramref name="message"/> names the place.</param>
    /// <param name="message">The message that names the place when <paramref name="siteId"/> is 0.</param>
    public static void Append(ref TranspileContext context, string trapName, string errorCode, uint siteId,
        string message)
    {
        var writer = context.Emitter.Writer;
        var guards = context.Emitter.Dialect.IsCuda ? context.Emitter.ActiveGuards : null;
        var unwinds = guards is { Count: > 0 };
        var name = unwinds ? GSharpInternalNames.Prelude.TrapUnwind : trapName;
        writer.Append($"{name}(GSharpErrorCode::{errorCode}, ");
        if (siteId != 0u)
        {
            writer.Append($"{siteId}u");
        }
        else if (unwinds)
        {
            writer.Append("__gsharp_builtin_message_id(");
            CppStringLiteral.Append(writer, message);
            writer.Append(")");
        }
        else
        {
            CppStringLiteral.Append(writer, message);
        }

        if (unwinds && guards is not null)
        {
            // Innermost first
            for (var i = guards.Count - 1; i >= 0; i--)
            {
                writer.Append($", {guards[i]}");
            }
        }

        writer.Append(")");
    }
}
