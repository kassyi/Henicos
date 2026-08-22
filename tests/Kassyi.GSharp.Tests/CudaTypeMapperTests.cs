using Kassyi.GSharp.Transpiler;
using Mono.Cecil;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class CudaTypeMapperTests
{
    private TypeReference CreateTypeReference(string fullName)
    {
        var module = ModuleDefinition.CreateModule("Dummy", ModuleKind.Dll);
        var typeSys = module.TypeSystem;
        
        return fullName switch
        {
            "System.Int32" => typeSys.Int32,
            "System.Single" => typeSys.Single,
            "System.Double" => typeSys.Double,
            "System.Boolean" => typeSys.Boolean,
            "System.String" => typeSys.String,
            "System.Object" => typeSys.Object,
            _ => new TypeReference(string.Empty, fullName, module, null)
        };
    }

    [Theory]
    [InlineData("System.Int32", "int")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Boolean", "bool")]
    public void MapType_ShouldMapSupportedTypes(string typeName, string expectedCudaType)
    {
        var typeRef = CreateTypeReference(typeName);
        var actual = CudaTypeMapper.MapType(typeRef);
        Assert.Equal(expectedCudaType, actual);
    }

    [Fact]
    public void MapType_ShouldFallbackToTypeName_ForUnsupportedType()
    {
        var typeRef = CreateTypeReference("System.String");
        var actual = CudaTypeMapper.MapType(typeRef);
        Assert.Equal("String", actual); // Returns the type name for non-primitives
    }
}
