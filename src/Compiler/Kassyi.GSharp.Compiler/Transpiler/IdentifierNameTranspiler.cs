using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a simple name (a local, a parameter, a field, a property or a method group) to CUDA C++.
/// </summary>
/// <remarks>
/// A name that refers to a span or array method without calling it is failed before this runs
/// (<see cref="Rules.ExpressionInterceptors.UninvokedSpanMethodInterceptor"/>).
/// </remarks>
internal static class IdentifierNameTranspiler
{
    /// <summary>Writes <paramref name="node"/>: its override, intrinsic, constant, dereference or C++ name.</summary>
    public static void Transpile(IdentifierNameSyntax node, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetSymbol(node);
        if (context.Emitter.TryGetOverride(node.Identifier.Text, symbol, out var overrideValue))
        {
            overrideValue.WriteTo(context.Visitor);
            return;
        }

        if (TryAppendUnqualifiedIntrinsic(symbol, ref context))
        {
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            ConstantAtUse.Append(node, field.Type, field.ConstantValue, ref context);
            return;
        }

        if (IsDereferencedReference(symbol))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append('*');
                CppNameResolver.AppendSymbolName(symbol, ref context);
            }

            return;
        }

        AppendName(node, symbol, ref context);
    }

    // A Core intrinsic named without its type (using static Kassyi.GSharp.Core.Gpu) maps as Gpu.<name> does (#565);
    // so does a Math or MathF member (using static System.Math), which ProhibitedApiRule allows when it is mapped
    // (#899)
    private static bool TryAppendUnqualifiedIntrinsic(ISymbol? symbol, ref TranspileContext context) =>
        symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol &&
        symbol is { IsStatic: true, ContainingType: { } owner } &&
        (owner.ContainingNamespace.IsCoreNamespace() || owner.IsSystemMathType()) &&
        MemberAccessExpressionTranspilerRule.TryTranspileIntrinsic(symbol, ref context);

    // A ref or out parameter, a ref local, or an in / ref readonly parameter of a device function is a C++ pointer
    private static bool IsDereferencedReference([NotNullWhen(true)] ISymbol? symbol) =>
        symbol switch
        {
            IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true } => true,
            IParameterSymbol
            {
                RefKind: RefKind.In or RefKind.RefReadOnlyParameter,
                ContainingSymbol: IMethodSymbol method
            } => !method.HasAttribute(KnownAttributes.GpuKernel),
            _ => false
        };

    private static void AppendName(IdentifierNameSyntax node, ISymbol? symbol, ref TranspileContext context)
    {
        // [WHY] Lowering rewrites hoisted locals into references that carry the declarator's original span.
        symbol ??= context.Query.SemanticModel.GetDeclaredSymbol<ISymbol>(node);
        FailUnemittableReference(node, symbol, ref context);
        if (UnemittedMethodCall.TryFailMethodGroup(node, symbol, ref context))
        {
            return;
        }

        // A member function reads a captured primary constructor parameter from its hidden field (#495)
        if (symbol is IParameterSymbol parameter &&
            CppNameResolver.IsCapturedParameterReference(parameter, ref context))
        {
            context.Emitter.Writer.AppendCppName(CppName.Member(parameter.Name));
            return;
        }

        if (IsConstructorSelfMember(node, symbol, ref context))
        {
            context.Emitter.Writer.Append(GSharpInternalNames.Lowering.ConstructorSelf).Append('.');
        }

        var name = symbol is IPropertySymbol property &&
                   PropertyAccessorCall.WritesBackingField(node, symbol, context.Emitter.Dialect)
            ? CppNameResolver.ResolveBackingField(property)
            : CppNameResolver.Resolve(
                symbol,
                node.Identifier.ValueText,
                context.Emitter.Dialect,
                LoweringIdentity.GetRegion(node)
            );
        if (name.Kind == CppNameKind.Verbatim && TryGetMappedInitializerMember(node, symbol, ref context) is { } mapped)
        {
            context.Emitter.Writer.Append(mapped);
            return;
        }

        // <struct type>((*this)).<getter>(): a readonly member calls a getter that may change the struct on a copy
        // (#610)
        if (PropertyAccessorCall.PlanCopyType(node, symbol, ref context) is { } copyType)
        {
            context.Emitter.Dialect.AppendType(ref context, copyType);
            context.Emitter.Writer.Append("((*this)).");
        }

        context.Emitter.Writer.AppendCppName(name);
        PropertyAccessorCall.AppendCallIfGetter(node, symbol, ref context);
    }

    // The member named by an object initializer (new Vector3(v) { Y = l }) of a type whose members the dialect maps, as
    // a member access to it would be written (__gsharp_tmp.y)
    private static string? TryGetMappedInitializerMember(IdentifierNameSyntax node, ISymbol? symbol,
        ref TranspileContext context) =>
        node.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment &&
        assignment.Left == node &&
        symbol is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } &&
        symbol.ContainingType is { } type &&
        context.Emitter.Dialect.TryMapMemberAccess(type, node.Identifier.ValueText, out var mappedName, out var isCall) &&
        !isCall
            ? mappedName
            : null;

    // A host static member and a method that takes the dynamic shared memory table named without a call are recorded
    // as failures; the name is still written
    private static void FailUnemittableReference(IdentifierNameSyntax node, ISymbol? symbol,
        ref TranspileContext context)
    {
        if (symbol != null &&
            GpuCodeConstraints.IsHostStaticMemberReference(
                node,
                symbol,
                context.Query.SemanticModel,
                context.Emitter.Dialect
            ))
        {
            context.Reporter.Fail(
                GSharpDiagnostics.StaticMemberAccessNotSupported,
                node,
                symbol.ContainingType?.Name ?? string.Empty,
                symbol.Name);
        }

        if (symbol is IMethodSymbol method)
        {
            DynamicSharedMemoryEmitter.FailIfReferencedWithoutCall(node, method, ref context);
            NonNullProof.FailIfReferencedWithoutCall(node, method, ref context);
        }
    }

    // In a lowered struct constructor function, unqualified instance members belong to __gsharp_self
    private static bool IsConstructorSelfMember(IdentifierNameSyntax node, ISymbol? symbol,
        ref TranspileContext context) =>
        context.Emitter.StructConstructorType is { } ctorType &&
        symbol is IFieldSymbol { IsStatic: false, IsConst: false } or IPropertySymbol { IsStatic: false } &&
        symbol.ContainingType != null &&
        symbol.ContainingType.OriginalDefinition.IsEqualTo(ctorType.OriginalDefinition) &&
        node.Parent is not AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax };
}
