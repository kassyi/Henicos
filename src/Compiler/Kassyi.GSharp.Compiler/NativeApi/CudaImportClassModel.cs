namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Value model representing a group of <c>[CudaImport]</c> methods in a single class declaration.
/// </summary>
/// <param name="HintName">The hint name of the generated part of the class.</param>
/// <param name="NamespaceName">The namespace containing the class declaration.</param>
/// <param name="TypeDeclarations">
/// The partial declarations of the class and of the types that contain it, outermost first.
/// </param>
/// <param name="TopLevelTypeName">The metadata name of the class when a YAML binding can extend it too.</param>
/// <param name="Methods">The collection of import methods contained within this class.</param>
internal readonly record struct CudaImportClassModel(
    string HintName,
    string NamespaceName,
    EquatableArray<string> TypeDeclarations,
    string? TopLevelTypeName,
    EquatableArray<CudaImportMethodModel> Methods);
