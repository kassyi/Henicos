namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Device iterators: the shape of the method and the bodies of <c>foreach</c> loops over one (<see cref="KernelBarrierValidator"/>).</summary>
internal static class DeviceIteratorChecks
{
    internal static void ValidateDeviceIteratorHeader(KernelBarrierScan scan)
    {
        if (!scan.IsDeviceIterator ||
            scan.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(scan.Method) is not { } methodSymbol)
        {
            return;
        }

        if (!methodSymbol.IsStatic)
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                scan.Method.Identifier.GetLocation(),
                methodSymbol.Name,
                "iterator methods must be static");
            return;
        }

        if (methodSymbol.IsGenericMethod)
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                scan.Method.Identifier.GetLocation(),
                methodSymbol.Name,
                "generic iterator methods are not supported");
            return;
        }

        if (!IteratorTypeResolver.IsGenericIEnumerable(methodSymbol.ReturnType, out var elementType))
        {
            var formattedReturnType = IteratorTypeResolver.FormatType(methodSymbol.ReturnType);
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                scan.Method.Identifier.GetLocation(),
                methodSymbol.Name,
                $"return type must be System.Collections.Generic.IEnumerable<T>, not '{formattedReturnType}'");
            return;
        }

        if (IteratorTypeResolver.TryGetUnsupportedElementTypeReason(elementType, out var reason))
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                scan.Method.Identifier.GetLocation(),
                methodSymbol.Name,
                reason);
            return;
        }

        if (IteratorTypeResolver.IsRecursiveIterator(methodSymbol, scan.SemanticModel.Compilation))
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                scan.Method.Identifier.GetLocation(),
                methodSymbol.Name,
                "recursive iterators are not supported");
        }
    }

    internal static bool IsInsideLocalOrAnonymousFunction(KernelBarrierScan scan, SyntaxNode node)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != scan.Method; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInsideIteratorForEachBody(KernelBarrierScan scan, SyntaxNode node)
    {
        SyntaxNode child = node;
        for (var ancestor = node.Parent;
             ancestor != null && ancestor != scan.Method;
             child = ancestor, ancestor = ancestor.Parent)
        {
            if (ancestor is ForEachStatementSyntax forEach &&
                child == forEach.Statement &&
                IteratorTypeResolver.IsIteratorForEachSource(forEach.Expression, scan.SemanticModel))
            {
                return true;
            }
        }

        return false;
    }
}
