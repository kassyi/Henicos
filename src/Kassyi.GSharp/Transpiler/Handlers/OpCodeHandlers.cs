using Mono.Cecil;
using Mono.Cecil.Cil;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler.Handlers;

public interface IOpCodeHandler
{
    IEnumerable<Code> SupportedOpCodes
    {
        get;
    }
    void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method);
}

public static class OpCodeHelper
{
    public static string GetArgName(Instruction ins, MethodDefinition method)
    {
        if (ins.Operand is ParameterDefinition paramDef)
        {
            return paramDef.Name;
        }

        var index = ins.OpCode.Code switch { Code.Ldarg_0 => 0, Code.Ldarg_1 => 1, Code.Ldarg_2 => 2, Code.Ldarg_3 => 3, _ => Convert.ToInt32(ins.Operand) };
        return method.Parameters[index].Name;
    }

    public static int GetLocIndex(Instruction ins)
    {
        return ins.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => 0,
            Code.Ldloc_1 or Code.Stloc_1 => 1,
            Code.Ldloc_2 or Code.Stloc_2 => 2,
            Code.Ldloc_3 or Code.Stloc_3 => 3,
            _ when ins.Operand is VariableDefinition varDef => varDef.Index,
            _ => Convert.ToInt32(ins.Operand)
        };
    }
}

public class LoadArgumentHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3, Code.Ldarg_S, Code.Ldarg };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
        => stack.Push(new VariableNode(OpCodeHelper.GetArgName(ins, method)));
}

public class LoadConstantHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Ldc_I4, Code.Ldc_I4_S, Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3, Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7, Code.Ldc_I4_8, Code.Ldc_I4_M1 };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
    {
        var value = ins.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            _ => Convert.ToInt32(ins.Operand)
        };
        stack.Push(new LiteralNode(value));
    }
}

public class BinaryOpHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Add, Code.Sub, Code.Mul, Code.Div, Code.Rem, Code.Ceq, Code.Cgt, Code.Clt };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
    {
        var op = ins.OpCode.Code switch
        {
            Code.Add => BinaryOperator.Add,
            Code.Sub => BinaryOperator.Sub,
            Code.Mul => BinaryOperator.Mul,
            Code.Div => BinaryOperator.Div,
            Code.Rem => BinaryOperator.Mod,
            Code.Ceq => BinaryOperator.Eq,
            Code.Cgt => BinaryOperator.Gt,
            Code.Clt => BinaryOperator.Lt,
            _ => throw new NotImplementedException()
        };
        var right = stack.Pop();
        var left = stack.Pop();
        stack.Push(new BinaryOpNode(op, left, right));
    }
}

public class StoreArgumentHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Starg, Code.Starg_S };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
        => stmts.Add(new AssignNode(OpCodeHelper.GetArgName(ins, method), stack.Pop()));
}

public class LocalVariableHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Stloc_0, Code.Stloc_1, Code.Stloc_2, Code.Stloc_3, Code.Stloc_S, Code.Stloc, Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3, Code.Ldloc_S, Code.Ldloc };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
    {
        var name = "loc_" + OpCodeHelper.GetLocIndex(ins);
        if (ins.OpCode.Code.ToString().StartsWith("Stloc"))
        {
            stmts.Add(new AssignNode(name, stack.Pop()));
        }
        else
        {
            stack.Push(new VariableNode(name));
        }
    }
}

public class ArrayHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Ldelem_U4, Code.Ldelem_I4, Code.Ldelem_Ref, Code.Stelem_I4, Code.Stelem_Ref };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
    {
        if (ins.OpCode.Code.ToString().StartsWith("Ldelem"))
        {
            var index = stack.Pop();
            var arrayRef = stack.Pop();
            stack.Push(new ArrayAccessNode((arrayRef as VariableNode)?.Name ?? "unknown_array", index));
        }
        else
        {
            var value = stack.Pop();
            var index = stack.Pop();
            var arrayRef = stack.Pop();
            stmts.Add(new ArrayAssignNode((arrayRef as VariableNode)?.Name ?? "unknown_array", index, value));
        }
    }
}

public class MethodCallHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Call };
    public IEnumerable<Code> SupportedOpCodes => s_codes;

    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition callerMethod)
    {
        throw new NotSupportedException("Use the overload with AstContext");
    }

    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition callerMethod, AstContext ctx)
    {
        var methodRef = (MethodReference)ins.Operand;
        var resolvedMethod = methodRef.Resolve();
        
        bool isGpuKernel = resolvedMethod?.CustomAttributes.Any(a => a.AttributeType.Name == "GpuKernelAttribute") == true;
        
        bool isInline = false;
        if (resolvedMethod != null)
        {
            isInline = (resolvedMethod.ImplAttributes & MethodImplAttributes.AggressiveInlining) != 0;
        }

        if (isInline && resolvedMethod != null && resolvedMethod.HasBody)
        {
            // Perform inlining
            var args = new IAstNode[methodRef.Parameters.Count];
            for (var i = args.Length - 1; i >= 0; i--)
            {
                args[i] = stack.Pop();
            }

            string prefix = ctx.GetNextInlinePrefix();
            string endLabel = prefix + "end";
            string returnVar = null;

            if (methodRef.ReturnType.FullName != "System.Void")
            {
                returnVar = prefix + "ret";
                ctx.LocalVariables.Add(returnVar);
            }

            // Bind arguments
            for (int i = 0; i < args.Length; i++)
            {
                var argName = prefix + resolvedMethod.Parameters[i].Name;
                ctx.LocalVariables.Add(argName);
                stmts.Add(new AssignNode(argName, args[i]));
            }

            var inlinedBlock = ctx.Builder.ParseMethod(resolvedMethod, ctx);
            var renamer = new AstRenamerVisitor(prefix, endLabel, returnVar, ctx);
            renamer.Visit(inlinedBlock);
            
            // Add transformed statements
            stmts.Add(new BlockNode(renamer.TransformedStatements.ToList()));
            stmts.Add(new LabelNode(endLabel));

            if (returnVar != null)
            {
                stack.Push(new VariableNode(returnVar));
            }
            return;
        }

        if (!isGpuKernel)
        {
            throw new NotSupportedException($"Unsupported method call: {methodRef.FullName}. Only [GpuKernel] or [MethodImpl(AggressiveInlining)] methods are allowed.");
        }

        var kernelArgs = new IAstNode[methodRef.Parameters.Count];
        for (var i = kernelArgs.Length - 1; i >= 0; i--)
        {
            kernelArgs[i] = stack.Pop();
        }

        var callNode = new MethodCallNode(methodRef.Name, kernelArgs);
        if (methodRef.ReturnType.FullName == "System.Void")
        {
            stmts.Add(callNode);
        }
        else
        {
            stack.Push(callNode);
        }
    }
}

public class BranchHandler : IOpCodeHandler
{
    private static readonly Code[] s_codes = { Code.Br, Code.Br_S, Code.Leave, Code.Leave_S, Code.Blt, Code.Blt_S, Code.Blt_Un, Code.Blt_Un_S, Code.Brtrue, Code.Brtrue_S, Code.Brfalse, Code.Brfalse_S, Code.Beq, Code.Beq_S, Code.Ble, Code.Ble_S, Code.Ble_Un, Code.Ble_Un_S, Code.Bgt, Code.Bgt_S, Code.Bgt_Un, Code.Bgt_Un_S, Code.Bge, Code.Bge_S, Code.Bge_Un, Code.Bge_Un_S, Code.Bne_Un, Code.Bne_Un_S };
    public IEnumerable<Code> SupportedOpCodes => s_codes;
    public void Handle(Instruction ins, Stack<IAstNode> stack, List<IAstNode> stmts, MethodDefinition method)
    {
        var target = (Instruction)ins.Operand;
        if (ins.OpCode.Code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S)
        {
            stmts.Add(new GotoNode($"IL_{target.Offset:x4}"));
            return;
        }

        var condition = stack.Pop();
        if (ins.OpCode.Code is Code.Brtrue or Code.Brtrue_S) { }
        else if (ins.OpCode.Code is Code.Brfalse or Code.Brfalse_S)
        {
            condition = new BinaryOpNode(BinaryOperator.Eq, condition, new LiteralNode(0));
        }
        else
        {
            var left = stack.Pop();
            var op = ins.OpCode.Code switch
            {
                Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => BinaryOperator.Lt,
                Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => BinaryOperator.Lte,
                Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => BinaryOperator.Gt,
                Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => BinaryOperator.Gte,
                Code.Beq or Code.Beq_S => BinaryOperator.Eq,
                Code.Bne_Un or Code.Bne_Un_S => BinaryOperator.Neq,
                _ => BinaryOperator.Eq
            };
            condition = new BinaryOpNode(op, left, condition);
        }
        stmts.Add(new IfGotoNode(condition, $"IL_{target.Offset:x4}"));
    }
}
