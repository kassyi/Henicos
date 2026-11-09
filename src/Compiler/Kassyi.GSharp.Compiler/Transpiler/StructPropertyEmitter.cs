using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits a computed property of a struct (<see cref="StructAbiMembers.IsComputedProperty"/>) as getter and setter
/// member functions under the C++ name of the property.
/// </summary>
internal static class StructPropertyEmitter
{
    /// <summary>
    /// Writes the getter and, when the property has one with a body, the setter of <paramref name="property"/>.
    /// </summary>
    public static void WriteComputedProperty(IPropertySymbol property, ref TranspileContext context)
    {
        WriteComputedPropertyGetter(property, ref context);
        WriteComputedPropertySetter(property, ref context);
    }

    // __device__ inline <type> <property name>() [const] { <getter body> }
    // [WHY] The getter keeps the C++ name of the property (CppNameResolver), so the implicit `A` and `Sum` in a getter
    // body resolve to the members of the struct, and a read elsewhere only appends the call
    // (ComputedPropertyGetterCall). A getter that may change its struct is not const (#610); C# calls it on a copy of a
    // readonly receiver, which the call writes (ComputedPropertyGetterCall.CallsOnCopy).
    private static void WriteComputedPropertyGetter(IPropertySymbol property, ref TranspileContext context)
    {
        // A struct from metadata or from another compilation has no getter body to bind here.
        var compilation = context.Query.SemanticModel.Compilation;
        if (!property.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !TryGetGetterBody(declaration, out var block, out var expression))
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, property.Type);
        context.Emitter.Writer.Append(' ')
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect))
            .Append(ComputedPropertyGetterCall.IsConstGetter(property, compilation) ? "() const " : "() ");

        TranspilePropertyAccessorBody(declaration.SyntaxTree, block, expression, isVoidExpression: false, ref context);
    }

    // __device__ inline void <property name>(<type> <value parameter>) { <setter body> }
    // [WHY] The setter overloads the C++ name of the property with one parameter, so assignments and object / `with`
    // initializers call it by appending `(<rhs>)` after the member name (ComputedPropertyGetterCall).
    private static void WriteComputedPropertySetter(IPropertySymbol property, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        if (property.SetMethod is not { Parameters: [var valueParameter] } ||
            !property.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !TryGetSetterBody(declaration, out var block, out var expression))
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
        TranspilePropertyAccessorBody(declaration.SyntaxTree, block, expression, isVoidExpression: true, ref context);
    }

    private static void TranspilePropertyAccessorBody(SyntaxTree syntaxTree, BlockSyntax? block,
        ExpressionSyntax? expression, bool isVoidExpression, ref TranspileContext context)
    {
        // [WHY] The struct may be declared in another file than the kernel, so its body is bound with a model of its
        // own tree.
        var saved = SemanticModelSwitch.Enter(ref context, syntaxTree);
        try
        {
            LambdaTranspilerHelper.TranspileLambdaBody(ref context, block, expression, isVoidExpression);
        }
        finally
        {
            saved.Restore(ref context);
        }
    }

    // The body of `=> expr`, `get => expr` or `get { ... }`
    private static bool TryGetGetterBody(PropertyDeclarationSyntax declaration, out BlockSyntax? block,
        out ExpressionSyntax? expression)
    {
        block = null;
        expression = declaration.ExpressionBody?.Expression;
        if (expression != null || declaration.AccessorList is not { } accessorList)
        {
            return expression != null;
        }

        var getter = accessorList.Accessors
            .AsStackEnumerable()
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        block = getter?.Body;
        expression = getter?.ExpressionBody?.Expression;
        return block != null || expression != null;
    }

    // The body of `set => expr`, `set { ... }`, `init => expr` or `init { ... }`
    private static bool TryGetSetterBody(PropertyDeclarationSyntax declaration, out BlockSyntax? block,
        out ExpressionSyntax? expression)
    {
        block = null;
        expression = null;
        if (declaration.AccessorList is not { } accessorList)
        {
            return false;
        }

        var setter = accessorList.Accessors
            .AsStackEnumerable()
            .FirstOrDefault(static accessor =>
                accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                accessor.IsKind(SyntaxKind.InitAccessorDeclaration));
        block = setter?.Body;
        expression = setter?.ExpressionBody?.Expression;
        return block != null || expression != null;
    }
}
