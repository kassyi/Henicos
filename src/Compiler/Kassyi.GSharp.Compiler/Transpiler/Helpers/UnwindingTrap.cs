namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The call that a failed check writes. On CUDA, inside the scope of a <c>using</c>, the thread ends with
/// <c>exit</c>, which runs no destructor, so the call passes the guards of the scopes the check leaves to
/// <c>__gsharp_trap_unwind</c>, which records the failure, disposes them (the innermost first, as C# runs the
/// <c>finally</c> blocks while the exception leaves) and ends the thread. Anywhere else it is the plain trap call.
/// </summary>
internal static class UnwindingTrap
{
    /// <summary>
    /// The place of a failed check, decided before it is written: a site of the kernel's error site table, or the
    /// message that names the place (a device function has no table).
    /// </summary>
    /// <param name="Id">The site of the kernel's error site table, or 0 when <paramref name="Message"/> is used.</param>
    /// <param name="Message">The message that names the place.</param>
    internal readonly record struct TrapSite(uint Id, string Message)
    {
        /// <summary>The site <paramref name="id"/>, or the message when the id is 0.</summary>
        public static TrapSite Of(uint id, string message) => id != 0u ? new TrapSite(id, "") : new TrapSite(0u, message);

        /// <summary>A place that the message names.</summary>
        public static TrapSite OfMessage(string message) => new(0u, message);
    }

    /// <summary>
    /// Appends <c>&lt;trap&gt;(GSharpErrorCode::&lt;code&gt;, &lt;site&gt;u)</c> (or the message as a string literal), or
    /// <c>__gsharp_trap_unwind(GSharpErrorCode::&lt;code&gt;, &lt;site&gt;u, &lt;guards&gt;)</c>.
    /// </summary>
    /// <param name="context">The context whose writer receives the call, and whose guards are in scope.</param>
    /// <param name="trapName">The plain trap function of the place (<c>__gsharp_trap</c>, <c>__gsharp_trap_abort</c>).</param>
    /// <param name="errorCode">The <c>GSharpErrorCode</c> member.</param>
    /// <param name="site">The place of the check.</param>
    public static void Append(ref TranspileContext context, string trapName, string errorCode, TrapSite site)
    {
        var writer = context.Emitter.Writer;
        IReadOnlyList<string> guards = context.Emitter.Dialect.IsCuda ? context.Emitter.ActiveGuards : [];
        var unwinds = guards.Count > 0;
        var name = unwinds ? GSharpInternalNames.Prelude.TrapUnwind : trapName;
        writer.Append($"{name}(GSharpErrorCode::{errorCode}, ");
        if (site.Id != 0u)
        {
            writer.Append($"{site.Id}u");
        }
        else if (unwinds)
        {
            writer.Append("__gsharp_builtin_message_id(");
            CppStringLiteral.Append(writer, site.Message);
            writer.Append(")");
        }
        else
        {
            CppStringLiteral.Append(writer, site.Message);
        }

        // Innermost first
        for (var i = guards.Count - 1; i >= 0; i--)
        {
            writer.Append($", {guards[i]}");
        }

        writer.Append(")");
    }
}
