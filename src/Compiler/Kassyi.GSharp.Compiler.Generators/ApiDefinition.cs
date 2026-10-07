using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Kassyi.GSharp.Compiler.Generators;

public class NativeApiDefinition
{
    public string Namespace { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string? LibraryName { get; set; }
    public List<ApiMethodDefinition> Methods { get; set; } = new();
}

public class ApiMethodDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? NativeName { get; set; }
    public string? ClassName { get; set; }
    public string? EntryPoint { get; set; }
    public string? StringMarshalling { get; set; }
    public bool IsUnsafe { get; set; }
    
    public string? Summary { get; set; }
    public TypeTemplateDefinition? TypeTemplate { get; set; }
    public string ReturnType { get; set; } = "void";
    public string? ReturnDescription { get; set; }
    public List<ParameterDefinition> Parameters { get; set; } = new();
}

public class TypeTemplateDefinition
{
    public string Placeholder { get; set; } = "T";
    public List<string> Types { get; set; } = new();
}

public class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
}
