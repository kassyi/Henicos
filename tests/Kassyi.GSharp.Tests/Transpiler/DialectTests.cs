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
            var dialect = new Kassyi.GSharp.Compiler.Dialects.StandardCppDialect();
            Assert.NotNull(dialect);
            
            string mappedType;
            // Check that it mapped int correctly using reflection since the method is protected
            var method = typeof(Kassyi.GSharp.Compiler.Dialects.StandardCppDialect).GetMethod("TryMapType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var args = new object[] { "System.Int32", null };
            var result = (bool)method.Invoke(dialect, args);
            mappedType = (string)args[1];
            
            Assert.True(result);
            Assert.Equal("int", mappedType);
        }

        [Fact]
        public void CanLoadGSharpCuda()
        {
            var dialect = new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect();
            Assert.NotNull(dialect);
            
            // Check MethodPrefix
            Assert.Equal("__device__ inline ", dialect.MethodPrefix);
        }
    }
}
