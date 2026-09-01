using System;
using Xunit;
using Kassyi.GSharp.Compiler.Dialects;

namespace Kassyi.GSharp.Tests.DialectTests
{
    public class YamlDialectTests
    {
        [Fact]
        public void CanLoadStandardCpp()
        {
            var dialect = DynamicYamlDialect.Load("StandardCpp");
            Assert.NotNull(dialect);
            
            // Try to map System.Int32 using reflection or just checking the property
            var defField = typeof(DynamicYamlDialect).GetField("_def", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var def = (DialectDefinition)defField.GetValue(dialect)!;
            
            Assert.True(def.Types.ContainsKey("System.Int32"));
            Assert.Equal("int", def.Types["System.Int32"]);
        }

        [Fact]
        public void CanLoadGSharpCuda()
        {
            var dialect = DynamicYamlDialect.Load("GSharpCuda");
            Assert.NotNull(dialect);
            
            // Check MethodPrefix
            Assert.Equal("__device__ inline ", dialect.MethodPrefix);
        }
    }
}
