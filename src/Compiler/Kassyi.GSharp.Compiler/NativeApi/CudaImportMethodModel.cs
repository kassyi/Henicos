namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Value model representing a single method decorated with <c>[CudaImport]</c>.
/// </summary>
/// <param name="HintName">
/// The hint name of the generated part of the containing class (<see cref="GlobalCppNames.GetHintName"/>), unique per
/// class, which also groups the methods of one class.
/// </param>
/// <param name="NamespaceName">The namespace as C# source spells it, or empty for the global namespace.</param>
/// <param name="TypeDeclarations">
/// The partial declarations of the containing class and of the types that contain it, outermost first.
/// </param>
/// <param name="TopLevelTypeName">
/// The metadata name of the containing class when a YAML binding can extend it too (not nested, not generic).
/// </param>
/// <param name="MessageName">The method as messages name it (<c>Type.Method</c>).</param>
/// <param name="ModuleName">The fatbin module name specified in the attribute.</param>
/// <param name="EntryPoint">The kernel entry point function name in CUDA C++.</param>
/// <param name="PackContext">Whether parameters should be packed into a context struct before launching.</param>
/// <param name="Signature">The pre-rendered C# method signature.</param>
/// <param name="Plan">The kernel arguments and launch settings of the launcher.</param>
internal readonly record struct CudaImportMethodModel(
    string HintName,
    string NamespaceName,
    EquatableArray<string> TypeDeclarations,
    string? TopLevelTypeName,
    string MessageName,
    string ModuleName,
    string EntryPoint,
    bool PackContext,
    string Signature,
    ModuleKernelLaunchPlan Plan);
