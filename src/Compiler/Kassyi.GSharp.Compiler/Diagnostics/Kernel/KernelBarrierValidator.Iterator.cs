namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

// Device iterators: the shape of the method and the bodies of foreach loops over one
internal readonly ref partial struct KernelBarrierValidator
{
    private void ValidateDeviceIteratorHeader()
    {
        if (!_isDeviceIterator ||
            semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl) is not { } methodSymbol)
        {
            return;
        }

        if (!methodSymbol.IsStatic)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "iterator methods must be static");
            return;
        }

        if (methodSymbol.IsGenericMethod)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "generic iterator methods are not supported");
            return;
        }

        if (!IteratorTypeResolver.IsGenericIEnumerable(methodSymbol.ReturnType, out var elementType))
        {
            var formattedReturnType = IteratorTypeResolver.FormatType(methodSymbol.ReturnType);
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                $"return type must be System.Collections.Generic.IEnumerable<T>, not '{formattedReturnType}'");
            return;
        }

        if (IteratorTypeResolver.TryGetUnsupportedElementTypeReason(elementType, out var reason))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                reason);
            return;
        }

        if (IteratorTypeResolver.IsRecursiveIterator(methodSymbol, semanticModel.Compilation))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "recursive iterators are not supported");
        }
    }

    private bool IsInsideLocalOrAnonymousFunction(SyntaxNode node)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInsideIteratorForEachBody(SyntaxNode node)
    {
        SyntaxNode child = node;
        for (var ancestor = node.Parent;
             ancestor != null && ancestor != methodDecl;
             child = ancestor, ancestor = ancestor.Parent)
        {
            if (ancestor is ForEachStatementSyntax forEach &&
                child == forEach.Statement &&
                IteratorTypeResolver.IsIteratorForEachSource(forEach.Expression, semanticModel))
            {
                return true;
            }
        }

        return false;
    }
}
