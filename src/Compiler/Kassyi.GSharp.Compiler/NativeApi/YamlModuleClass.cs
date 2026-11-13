namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A class that a YAML <c>moduleName</c> binding extends, and the module whose field
/// (<see cref="AotModuleNames.GetModuleFieldName"/>) the binding declares in it. <c>[CudaImport]</c>
/// methods of the same module in that class use that field instead of declaring a second one.
/// </summary>
/// <param name="TypeName">
/// The metadata name of the class (<see cref="GetTypeName"/>): <c>&lt;namespace&gt;.&lt;class&gt;</c>, keywords not
/// escaped.
/// </param>
/// <param name="ModuleName">The module name as the binding writes it.</param>
internal readonly record struct YamlModuleClass(string TypeName, string ModuleName)
{
    /// <summary>
    /// The key of a class on both sides: <c>&lt;namespace&gt;.&lt;class&gt;</c>, or the class alone in the global
    /// namespace, with the names as the metadata spells them (keywords not escaped).
    /// </summary>
    public static string GetTypeName(string namespaceName, string className) =>
        namespaceName.Length == 0 ? className : namespaceName + "." + className;
}
