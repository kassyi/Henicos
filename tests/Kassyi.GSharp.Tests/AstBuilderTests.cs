using System;
using System.Linq;
using Kassyi.GSharp.Transpiler;
using Kassyi.GSharp.Transpiler.Ast;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Kassyi.GSharp.Tests
{
    public class AstBuilderTests
    {
        [Fact]
        public void ParseMethod_ShouldParseValidOpcodes()
        {
            using var module = ModuleDefinition.CreateModule("TestModule", ModuleKind.Dll);
            var method = new MethodDefinition("Test", MethodAttributes.Public, module.TypeSystem.Void);
            var body = new MethodBody(method);
            var processor = body.GetILProcessor();
            
            processor.Emit(OpCodes.Ldc_I4_1);
            processor.Emit(OpCodes.Ldc_I4_2);
            processor.Emit(OpCodes.Add);
            
            method.Body = body;
            
            var builder = new AstBuilder();
            var result = builder.ParseMethod(method);
            
            Assert.NotNull(result);
        }

        [Fact]
        public void ParseMethod_ShouldThrowNotSupportedException_ForUnknownOpcode()
        {
            using var module = ModuleDefinition.CreateModule("TestModule", ModuleKind.Dll);
            var method = new MethodDefinition("Test", MethodAttributes.Public, module.TypeSystem.Void);
            var body = new MethodBody(method);
            var processor = body.GetILProcessor();
            
            processor.Emit(OpCodes.Initblk);
            
            method.Body = body;
            
            var builder = new AstBuilder();
            
            var ex = Assert.Throws<NotSupportedException>(() => builder.ParseMethod(method));
            Assert.Contains("Unsupported IL OpCode", ex.Message);
        }
    }
}
