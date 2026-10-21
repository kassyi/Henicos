namespace Kassyi.GSharp.Compiler.Models;
using NativeApi;

/// <summary>
/// Value model representing a single method decorated with <c>[CudaImport]</c>.
/// </summary>
/// <param name="ClassKey">The qualified lookup key for the containing class (e.g. namespace and name).</param>
/// <param name="NamespaceName">The namespace name containing the method, or empty for global namespace.</param>
/// <param name="ClassHeader">The pre-rendered class declaration header string.</param>
/// <param name="MethodName">The name of the partial import method.</param>
/// <param name="ModuleName">The fatbin module name specified in the attribute.</param>
/// <param name="EntryPoint">The kernel entry point function name in CUDA C++.</param>
/// <param name="PackContext">Whether parameters should be packed into a context struct before launching.</param>
/// <param name="Signature">The pre-rendered C# method signature.</param>
/// <param name="Parameters">The launch parameters of the method.</param>
internal readonly record struct CudaImportMethodModel(
    string ClassKey,
    string NamespaceName,
    string ClassHeader,
    string MethodName,
    string ModuleName,
    string EntryPoint,
    bool PackContext,
    string Signature,
    EquatableArray<KernelLaunchParameter> Parameters);

/// <summary>
/// Value model representing a group of <c>[CudaImport]</c> methods in a single class declaration.
/// </summary>
/// <param name="NamespaceName">The namespace containing the class declaration.</param>
/// <param name="ClassName">The name of the class holding the import methods.</param>
/// <param name="ClassHeader">The pre-rendered class declaration header string.</param>
/// <param name="Methods">The collection of import methods contained within this class.</param>
internal readonly record struct CudaImportClassModel(
    string NamespaceName,
    string ClassName,
    string ClassHeader,
    EquatableArray<CudaImportMethodModel> Methods);
