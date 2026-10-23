namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Turns a read of a computed property of a struct that G# emits into a call of its getter: <c>s.Sum</c> becomes
/// <c>s.__gsharp_mb_Sum()</c>, and <c>Sum</c> inside another getter of the struct becomes <c>__gsharp_mb_Sum()</c>.
/// </summary>
/// <remarks>
/// <c>StructTranspiler</c> emits the getter as a member function under the C++ name of the property
/// (<see cref="CppNameResolver"/>), so the caller writes the receiver and the name as for any member, a <c>ref</c>
/// receiver included, and this appends the call. A computed property is defined by
/// <see cref="StructAbiMembers.IsComputedProperty"/>.
/// </remarks>
internal static class ComputedPropertyGetterCall
{
    /// <summary>Whether <paramref name="symbol"/> is a computed property that is read through its getter.</summary>
    public static bool IsGetter(ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol { ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.IsComputedProperty(property);

    /// <summary>Appends <c>()</c> after the name of <paramref name="symbol"/> when it is read through its getter.</summary>
    public static void AppendCallIfGetter(ISymbol? symbol, ref TranspileContext context) =>
        context.Emitter.Writer.AppendIf(IsGetter(symbol, context.Emitter.Dialect), "()");
}
