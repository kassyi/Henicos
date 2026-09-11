namespace Kassyi.GSharp.Compiler.VmObfuscation;

internal interface IVmStrategy
{
    void EmitBytecodeGeneration(SourceWriter writer, IMethodSymbol method, byte[] aesKey, Random rng,
        byte[] opcodes);

    void EmitVmExecution(SourceWriter writer, IMethodSymbol method, byte[] opcodes);
    void EmitClearLogic(SourceWriter writer);
}
