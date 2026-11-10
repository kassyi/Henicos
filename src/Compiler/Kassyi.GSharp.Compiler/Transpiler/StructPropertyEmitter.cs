using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the accessors of a struct property (<see cref="StructAbiMembers.HasAccessorFunctions"/>) as getter and setter
/// member functions under the C++ name of the property, from its <see cref="StructPropertyPlan"/>.
/// </summary>
internal static class StructPropertyEmitter
{
    /// <summary>Writes the getter and the setter of <paramref name="property"/> that its plan has.</summary>
    public static void WriteAccessors(IPropertySymbol property, ref TranspileContext context)
    {
        var plan = StructPropertyPlanner.Plan(property, context.Query.SemanticModel.Compilation);
        WriteGetter(property, plan, ref context);
        WriteSetter(property, plan, ref context);
    }

    // __device__ inline <type> <property name>() [const] { <getter body> }
    // [WHY] The getter keeps the C++ name of the property (CppNameResolver), so the implicit `A` and `Sum` in a getter
    // body resolve to the members of the struct, and a read elsewhere only appends the call
    // (ComputedPropertyGetterCall). A getter that may change its struct is not const (#610); C# calls it on a copy of a
    // readonly receiver, which the call writes (ComputedPropertyGetterCall.PlanCopyType).
    private static void WriteGetter(IPropertySymbol property, in StructPropertyPlan plan, ref TranspileContext context)
    {
        if (plan.Getter.Body == PropertyAccessorBody.None)
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, property.Type);
        context.Emitter.Writer.Append(' ')
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect))
            .Append(plan.IsConstGetter ? "() const " : "() ");

        if (plan.Getter.Body == PropertyAccessorBody.ReadsBackingField)
        {
            // { return <backing field>; }
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.Append("return ")
                    .AppendCppName(CppNameResolver.ResolveBackingField(property))
                    .AppendLine(";");
            }

            return;
        }

        TranspileWrittenBody(plan.Getter, isVoidExpression: false, ref context);
    }

    // __device__ inline void <property name>(<type> <value parameter>) { <setter body> }
    // [WHY] The setter overloads the C++ name of the property with one parameter, so assignments and object / `with`
    // initializers call it by appending `(<rhs>)` after the member name (ComputedPropertyGetterCall).
    private static void WriteSetter(IPropertySymbol property, in StructPropertyPlan plan, ref TranspileContext context)
    {
        if (plan.Setter.Body == PropertyAccessorBody.None || plan.ValueParameter is not { } valueParameter)
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline void ")
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect));
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, property.Type);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendSymbolName(valueParameter, ref context);
        }

        context.Emitter.Writer.Append(' ');
        if (plan.Setter.Body == PropertyAccessorBody.WritesBackingField)
        {
            // { <backing field> = <value parameter>; }
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendCppName(CppNameResolver.ResolveBackingField(property)).Append(" = ");
                CppNameResolver.AppendSymbolName(valueParameter, ref context);
                context.Emitter.Writer.AppendLine(";");
            }

            return;
        }

        TranspileWrittenBody(plan.Setter, isVoidExpression: true, ref context);
    }

    private static void TranspileWrittenBody(in PropertyAccessorPlan accessor, bool isVoidExpression,
        ref TranspileContext context)
    {
        // A written body always carries its tree (PropertyAccessorPlan.Written)
        var syntaxTree = accessor.SyntaxTree ??
                         throw new InvalidOperationException("A written accessor body has no syntax tree.");

        // [WHY] The struct may be declared in another file than the kernel, so its body is bound with a model of its
        // own tree.
        var saved = SemanticModelSwitch.Enter(ref context, syntaxTree);
        try
        {
            LambdaTranspilerHelper.TranspileLambdaBody(ref context, accessor.Block, accessor.Expression, isVoidExpression);
        }
        finally
        {
            saved.Restore(ref context);
        }
    }
}
