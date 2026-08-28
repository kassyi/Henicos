using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>Invocation transpiler rule that resolves extension method calls into static CUDA C++ function invocations.</summary>
internal sealed class ExtensionMethodTranspilerRule : IInvocationTranspilerRule
{
    public static readonly ExtensionMethodTranspilerRule Instance = new();

    private ExtensionMethodTranspilerRule()
    {
    }

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (node.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var targetSymbol = symbol.ReducedFrom ?? (symbol.IsExtensionMethod ? symbol : null);
        if (targetSymbol == null)
        {
            return false;
        }

        var exprSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
        if (exprSymbol is ITypeSymbol)
        {
            return false;
        }

        var methodDef = targetSymbol.OriginalDefinition;
        var hasGpuAttr = methodDef.GetAttributes().Any(a =>
            KnownAttributes.IsGpuDeviceFunction(a.AttributeClass?.Name) ||
            KnownAttributes.IsGpuKernel(a.AttributeClass?.Name));

        if (!hasGpuAttr)
        {
            if (!context.BclIntrinsicMap.ContainsKey(methodDef) &&
                !CudaIntrinsicsMapper.TryMap(symbol, out _))
            {
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node.GetLocation(),
                    $"Extension method '{symbol.Name}' lacks [GpuDeviceFunction] attribute"));
                return true;
            }
        }

        context.Writer.Append(symbol.Name);
        if (symbol.IsGenericMethod && symbol.TypeArguments.Length > 0)
        {
            context.Writer.Append("<");
            for (var i = 0; i < symbol.TypeArguments.Length; i++)
            {
                context.Writer.Append(CudaTypeMapper.MapType(symbol.TypeArguments[i]));
                if (i < symbol.TypeArguments.Length - 1)
                {
                    context.Writer.AppendComma();
                }
            }

            context.Writer.Append(">");
        }

        using (context.Writer.Parentheses())
        {
            var receiverParam = methodDef.Parameters.FirstOrDefault();
            var needsAddressOf = receiverParam != null &&
                                 (receiverParam.RefKind == RefKind.Ref || receiverParam.RefKind == RefKind.Out);

            if (needsAddressOf)
            {
                var isAlreadyPointer = exprSymbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } ||
                                       exprSymbol is ILocalSymbol { IsRef: true };

                if (!isAlreadyPointer)
                {
                    context.Writer.Append("&");
                }
            }

            context.Visitor.Visit(memberAccess.Expression);

            if (node.ArgumentList.Arguments.Count > 0)
            {
                context.Writer.AppendComma();
            }

            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }

        return true;
    }
}
