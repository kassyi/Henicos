#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Transpiles one method of a test compilation into target dialect C++ source, for tests that check the emitted text
/// or the transpile failures of a single method.
/// </summary>
public static class TranspileTestRunner
{
    /// <summary>Transpiles a method declared in the mega compilation by type and method name into target dialect C++ source.</summary>
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod(
        string typeName,
        string methodName,
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        var (semanticModel, methodSymbol) = RoslynTestUtility.GetMethodContext(typeName, methodName);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<TranspileFailure>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method from dynamic compilation inferred from the caller file into target dialect C++ source.</summary>
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        var pathList = new List<string> { callerFilePath };
        if (RoslynTestUtility.TryResolveKernelFilePath(callerFilePath, out var kernelFile) && kernelFile != null)
        {
            pathList.Add(kernelFile);
        }

        var dir = Path.GetDirectoryName(callerFilePath);
        if (dir != null)
        {
            var targetTypeKernelFile = Path.Combine(dir, $"{targetType.Name}.cu.cs");
            if (File.Exists(targetTypeKernelFile) && !pathList.Contains(targetTypeKernelFile))
            {
                pathList.Add(targetTypeKernelFile);
            }
        }

        var (semanticModel, methodSymbol) =
            RoslynTestUtility.GetMethodContext(targetType, methodName, pathList.ToArray());
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<TranspileFailure>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method declared on <typeparamref name="TTarget"/> into target dialect C++ source with type safety.</summary>
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod<TTarget>(
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        return TranspileMethod(typeof(TTarget), methodName, callerFilePath, isDevice, dialect);
    }
}
