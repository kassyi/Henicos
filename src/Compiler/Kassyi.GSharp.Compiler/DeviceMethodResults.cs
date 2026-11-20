using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The results of a device function that <see cref="GpuKernelGenerator"/> does not transpile: a generic declaration
/// that is left out (GSH0123), a failure that stopped it (GSH0102), or an internal error (GSH0048).
/// </summary>
internal static class DeviceMethodResults
{
    /// <summary>
    /// The result of a generic device function that <see cref="GenericDefinitions.IsLeftOut"/> leaves out: GSH0123 at
    /// the name of <paramref name="bodyDeclaration"/>, where the analyzer reports it, and a record that says the
    /// function was not transpiled (GSH0089 for a consumer).
    /// </summary>
    public static TranspiledDeviceMethodResult LeftOutGeneric(IMethodSymbol methodSymbol,
        MethodDeclarationSyntax bodyDeclaration, Compilation compilation)
    {
        var failure = new TranspileFailure(GSharpDiagnostics.GenericDeviceFunctionNotCompiled,
            bodyDeclaration.Identifier.GetLocation(), [methodSymbol.Name, GenericDefinitions.NotSupportedReason]);
        return NotTranspiled(methodSymbol, compilation, failure, CreateFailedFunctionRecord(methodSymbol, ""));
    }

    /// <summary>
    /// A device function that is not transpiled: both dialects state <paramref name="failure"/> as <c>#error</c>.
    /// </summary>
    public static TranspiledDeviceMethodResult NotTranspiled(IMethodSymbol methodSymbol, Compilation compilation,
        TranspileFailure failure, DeviceExportRecord export)
    {
        var errorSource = NativeModuleAssembler.FormatTranspileFailures([failure.Format()]);
        return new TranspiledDeviceMethodResult(
            HostClassHeader.Create(methodSymbol.ContainingType, compilation),
            methodSymbol.Name,
            "", errorSource, "", errorSource,
            default, default, "",
            Export: export);
    }

    /// <summary>A device function whose transpilation threw: the exception in both dialects (GSH0048).</summary>
    public static TranspiledDeviceMethodResult InternalError(IMethodSymbol methodSymbol, Compilation compilation,
        Exception exception)
    {
        var errorSource = $"/*\n{exception}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n";
        return new TranspiledDeviceMethodResult(
            HostClassHeader.Create(methodSymbol.ContainingType, compilation),
            methodSymbol.Name,
            "", errorSource, "", errorSource, default, default, exception.ToString());
    }

    /// <summary>
    /// The record of a function whose records could not be built: failed in both dialects, with the message.
    /// </summary>
    public static DeviceExportRecord CreateFailedFunctionRecord(IMethodSymbol method, string internalError)
    {
        var cppName = GlobalCppNames.GetFunctionName(method);
        var names = new DeviceExportRecord.ExportNames(
            CppName: cppName,
            CppFullName: GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            DefinedNames: default,
            DefinedIds: default,
            DefinedFullNames: default);
        return new DeviceExportRecord(
            DeviceExportRecord.FunctionKind,
            method.OriginalDefinition.GetDocumentationCommentId() ?? cppName,
            names,
            Cuda: DeviceExportRecord.DialectText.NotTranspiled,
            Cpp: DeviceExportRecord.DialectText.NotTranspiled,
            DeviceExportRecord.FunctionTraits.None,
            internalError);
    }
}
