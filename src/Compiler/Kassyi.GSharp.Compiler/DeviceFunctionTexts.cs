namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The text of a device function in both dialects and the sites of its throws, which go with it to every module that
/// places it.
/// </summary>
internal readonly record struct DeviceFunctionTexts(
    DeviceDialectText Cuda,
    DeviceDialectText Cpp,
    EquatableArray<ErrorSiteEntry> Sites)
{
    /// <summary>Transpiles the device function of <paramref name="plan"/> into CUDA C++ and standard C++.</summary>
    public static DeviceFunctionTexts Transpile(in MethodEmissionPlan plan)
    {
        var cudaFailures = new FailureCollector();
        var cppFailures = new FailureCollector();
        var cudaDialect = new GSharpCudaDialect();
        var cppDialect = new StandardCppDialect();

        var cudaSignature = MethodTranspiler.GenerateCudaSignature(in plan, cudaFailures.Fail, cudaDialect);
        var cppSignature = MethodTranspiler.GenerateCudaSignature(in plan, cppFailures.Fail, cppDialect);

        var cudaStructs = new StructSourceCollector();
        var cppStructs = new StructSourceCollector();
        var throwSites = new ErrorSiteTable();
        var cudaSource = MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
            out var cudaIncludes, cudaStructs, throwSites: throwSites);
        var cppSource = MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
            out var cppIncludes, cppStructs, throwSites: throwSites);

        return new DeviceFunctionTexts(
            new DeviceDialectText(cudaFailures, cudaSignature, cudaSource, cudaIncludes, cudaStructs),
            new DeviceDialectText(cppFailures, cppSignature, cppSource, cppIncludes, cppStructs),
            throwSites.Entries.AsEquatableArray());
    }
}
