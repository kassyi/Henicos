namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>Represents a diagnostic definition entry deserialized from diagnostics.json.</summary>
public sealed class DiagnosticDefinition
{
    /// <summary>Gets or sets the identifier name for the diagnostic descriptor property.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the diagnostic ID (e.g. GSH0001).</summary>
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the diagnostic title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Gets or sets the message format string.</summary>
    public string MessageFormat { get; set; } = "";

    /// <summary>Gets or sets the diagnostic category.</summary>
    public string Category { get; set; } = "";

    /// <summary>Gets or sets the optional long description of the diagnostic.</summary>
    public string Description { get; set; } = "";

    /// <summary>Gets or sets the default severity string.</summary>
    public string DefaultSeverity { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether the diagnostic is enabled by default.</summary>
    public bool IsEnabledByDefault
    {
        get; set;
    }

    /// <summary>Gets or sets a value indicating whether this diagnostic is obsolete.</summary>
    public bool IsObsolete
    {
        get; set;
    }
}
