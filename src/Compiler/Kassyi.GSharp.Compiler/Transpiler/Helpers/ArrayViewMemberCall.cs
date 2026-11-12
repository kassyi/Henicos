namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes the callee of a call of <c>Slice</c> or <c>CopyTo</c> on an <c>ArraySegment&lt;T&gt;</c> or
/// <c>ImmutableArray&lt;T&gt;</c> as the checked member of the array view, which reports a value without an array as C#
/// does (<see cref="ArrayViewMethods.GetChecks"/>):
/// <c>&lt;receiver&gt;.template CheckedSlice&lt;GSharpErrorCode::&lt;if default&gt;&gt;</c> or
/// <c>&lt;receiver&gt;.template CheckedCopyTo&lt;GSharpErrorCode::&lt;if default&gt;, GSharpErrorCode::&lt;if
/// null&gt;&gt;</c>. The standard call emission writes the arguments after it.
/// </summary>
/// <remarks>
/// [WHY] The view is one C++ type for every array-like C# type, and a default span is empty where a default segment has
/// no array, so the check is chosen by the C# type of the receiver here. It runs inside the member, after the receiver
/// and the arguments are evaluated, as the C# method checks after its call.
/// </remarks>
internal static class ArrayViewMemberCall
{
    /// <summary>
    /// Writes <paramref name="node"/>, the callee of an invocation of <paramref name="symbol"/>, when the method checks
    /// its receiver or destination.
    /// </summary>
    /// <returns>Whether <paramref name="node"/> was written.</returns>
    public static bool TryAppendCallee(MemberAccessExpressionSyntax node, ISymbol? symbol, ref TranspileContext context)
    {
        if (node.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != node ||
            symbol is not IMethodSymbol method || !ArrayViewMethods.IsViewMember(method) ||
            ArrayViewMethods.GetChecks(method) is not { HasChecks: true } checks)
        {
            return false;
        }

        var writer = context.Emitter.Writer;
        StackArrayLocals.AppendView(node.Expression, ref context);
        writer.Append(method.Name == "Slice" ? ".template CheckedSlice" : ".template CheckedCopyTo");
        using (writer.InlineScope("<", ">"))
        {
            writer.Append($"GSharpErrorCode::{GetCodeName(checks.IfDefault)}");
            writer.AppendIf(method.Name != "Slice", $", GSharpErrorCode::{GetCodeName(checks.IfDestinationNull)}");
        }

        return true;
    }

    private static string GetCodeName(ViewCheck check) => check switch
    {
        ViewCheck.None => "None",
        ViewCheck.InvalidOperation => "InvalidOperation",
        ViewCheck.NullReference => "NullReference",
        ViewCheck.ArgumentNull => "ArgumentNull",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
    };
}
