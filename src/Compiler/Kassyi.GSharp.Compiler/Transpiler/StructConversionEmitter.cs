namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits user-defined struct conversion operators (<c>implicit operator</c> and <c>explicit operator</c>) as
/// value-returning device functions (<c>__gsharp_fn_...__op_Implicit</c> / <c>__op_Explicit</c>).
/// </summary>
internal static class StructConversionEmitter
{
    /// <summary>
    /// Whether <paramref name="conversion"/> is a user-defined struct conversion operator (<c>implicit operator</c> or
    /// <c>explicit operator</c>) emitted as a value-returning device function (<c>__gsharp_fn_...__op_Implicit</c> /
    /// <c>__op_Explicit</c>). A <c>ref struct</c> (such as <c>GpuBigIntRef</c>) keeps its inline C++ member conversion
    /// operators for <c>[GpuOperatorMacro]</c> buffer forwarding.
    /// </summary>
    public static bool IsEmittedStructConversion(IMethodSymbol? conversion, Compilation compilation) =>
        conversion is { MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false } &&
        !conversion.HasAttribute(KnownAttributes.GpuOperatorMacro) &&
        conversion.ContainingType is { IsRefLikeType: false } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        (TryGetConversionDeclaration(conversion, out _) ||
         (DeviceExportTable.IsOutside(conversion, compilation) && DeviceExportTable.TryFind(conversion, out _)));

    public static bool TryGetConversionDeclaration(IMethodSymbol conversion,
        [NotNullWhen(true)] out ConversionOperatorDeclarationSyntax? declaration)
    {
        if (conversion.TryGetDeclaringSyntax<ConversionOperatorDeclarationSyntax>(out declaration))
        {
            return declaration.Body != null || declaration.ExpressionBody != null;
        }

        declaration = null;
        return false;
    }

    public static void WriteStructConversionOperators(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        if (structSym.IsRefLikeType)
        {
            return;
        }

        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in structSym.GetMembers())
        {
            if (member is IMethodSymbol conversion &&
                IsEmittedStructConversion(conversion, compilation) &&
                TryGetConversionDeclaration(conversion, out var declaration) &&
                compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                WriteStructConversionOperator(conversion, declaration, ref context);
            }
        }
    }

    public static void WriteStructConversionOperator(IMethodSymbol conversion,
        ConversionOperatorDeclarationSyntax declaration, ref TranspileContext context)
    {
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);
        var previousCtorType = context.Emitter.StructConstructorType;
        var previousCallableDepth = context.Emitter.CallableDepth;
        context.Emitter.StructConstructorType = null;
        context.Emitter.CallableDepth = 0;

        try
        {
            context.Emitter.Writer.Append("__device__ inline ");
            context.Emitter.Dialect.AppendType(ref context, conversion.ReturnType);
            context.Emitter.Writer.Append(' ').Append(GlobalCppNames.GetFunctionName(conversion));
            context.Visitor.Visit(declaration.ParameterList);
            context.Emitter.Writer.Append(' ');

            LambdaTranspilerHelper.TranspileLambdaBody(
                ref context,
                declaration.Body,
                declaration.ExpressionBody?.Expression,
                isVoidExpression: false);
        }
        finally
        {
            context.Emitter.StructConstructorType = previousCtorType;
            context.Emitter.CallableDepth = previousCallableDepth;
            context.Query = query;
            context.Reporter = reporter;
        }
    }
}
