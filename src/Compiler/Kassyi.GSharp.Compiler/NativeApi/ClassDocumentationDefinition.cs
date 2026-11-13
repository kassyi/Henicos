namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>The XML documentation of a class that methods are declared in.</summary>
internal sealed class ClassDocumentationDefinition
{
    /// <summary>Gets or sets the class, as a method's <c>className</c> names it (<c>Gpu.Unsafe</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the XML documentation, written as is after <c>///</c>.</summary>
    public string XmlDoc { get; set; } = string.Empty;
}
