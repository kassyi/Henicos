using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler
{
    public class AstBuilder
    {
        private readonly Dictionary<Code, Action<Instruction, Stack<IAstNode>, List<IAstNode>, MethodDefinition>> _handlers = new();

        public AstBuilder()
        {
            Register(Code.Nop, (ins, stack, stmts, method) => { /* Ignore Nop */ });

            Register(new[] { Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3, Code.Ldarg_S, Code.Ldarg }, 
                (ins, stack, stmts, method) => stack.Push(new VariableNode(GetArgName(ins, method))));

            Register(new[] { Code.Ldc_I4, Code.Ldc_I4_S, Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3, Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7, Code.Ldc_I4_8, Code.Ldc_I4_M1 }, 
                (ins, stack, stmts, method) => stack.Push(new LiteralNode(GetLdcI4Value(ins))));

            RegisterBinaryOp(Code.Add, BinaryOperator.Add);
            RegisterBinaryOp(Code.Sub, BinaryOperator.Sub);
            RegisterBinaryOp(Code.Mul, BinaryOperator.Mul);
            RegisterBinaryOp(Code.Div, BinaryOperator.Div);
            RegisterBinaryOp(Code.Rem, BinaryOperator.Mod);
            
            RegisterBinaryOp(Code.Ceq, BinaryOperator.Eq);
            RegisterBinaryOp(Code.Cgt, BinaryOperator.Gt);
            RegisterBinaryOp(Code.Clt, BinaryOperator.Lt);

            Register(new[] { Code.Starg, Code.Starg_S }, 
                (ins, stack, stmts, method) => stmts.Add(new AssignNode(GetArgName(ins, method), stack.Pop())));

            Register(new[] { Code.Stloc_0, Code.Stloc_1, Code.Stloc_2, Code.Stloc_3, Code.Stloc_S, Code.Stloc }, 
                (ins, stack, stmts, method) => stmts.Add(new AssignNode("loc_" + GetLocIndex(ins), stack.Pop())));

            Register(new[] { Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3, Code.Ldloc_S, Code.Ldloc }, 
                (ins, stack, stmts, method) => stack.Push(new VariableNode("loc_" + GetLocIndex(ins))));

            Register(Code.Call, (ins, stack, stmts, method) => 
            {
                var methodRef = (MethodReference)ins.Operand;
                var args = new IAstNode[methodRef.Parameters.Count];
                for (int i = args.Length - 1; i >= 0; i--)
                {
                    args[i] = stack.Pop();
                }
                
                var callNode = new MethodCallNode(methodRef.Name, args);
                
                if (methodRef.ReturnType.FullName == "System.Void")
                {
                    stmts.Add(callNode);
                }
                else
                {
                    stack.Push(callNode);
                }
            });

            Register(Code.Ret, (ins, stack, stmts, method) => { /* Ignore return */ });

            // Control flow graph support (Basic block placeholders)
            Register(new[] { 
                Code.Br, Code.Br_S, Code.Blt, Code.Blt_S, Code.Brtrue, Code.Brtrue_S, 
                Code.Brfalse, Code.Brfalse_S, Code.Beq, Code.Beq_S, Code.Ble, Code.Ble_S, 
                Code.Bgt, Code.Bgt_S, Code.Bge, Code.Bge_S, Code.Bne_Un, Code.Bne_Un_S 
            }, (ins, stack, stmts, method) => { /* TODO: CFG Support */ });
        }

        private void Register(Code code, Action<Instruction, Stack<IAstNode>, List<IAstNode>, MethodDefinition> handler)
            => _handlers[code] = handler;

        private void Register(IEnumerable<Code> codes, Action<Instruction, Stack<IAstNode>, List<IAstNode>, MethodDefinition> handler)
        {
            foreach (var code in codes)
                _handlers[code] = handler;
        }

        private void RegisterBinaryOp(Code code, BinaryOperator op)
        {
            _handlers[code] = (ins, stack, stmts, method) => 
            {
                var right = stack.Pop();
                var left = stack.Pop();
                stack.Push(new BinaryOpNode(op, left, right));
            };
        }

        public BlockNode ParseMethod(MethodDefinition method)
        {
            var statements = new List<IAstNode>();
            
            if (!method.HasBody)
                return new BlockNode(statements);

            var instructions = method.Body.Instructions;
            var stack = new Stack<IAstNode>();
            
            for (int i = 0; i < instructions.Count; i++)
            {
                var ins = instructions[i];
                
                if (_handlers.TryGetValue(ins.OpCode.Code, out var handler))
                {
                    handler(ins, stack, statements, method);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported IL OpCode: {ins.OpCode}");
                }
            }

            return new BlockNode(statements);
        }
        
        private static string GetArgName(Instruction ins, MethodDefinition method)
        {
            if (ins.Operand is ParameterDefinition paramDef)
                return paramDef.Name;
                
            int index = ins.OpCode.Code switch
            {
                Code.Ldarg_0 => 0,
                Code.Ldarg_1 => 1,
                Code.Ldarg_2 => 2,
                Code.Ldarg_3 => 3,
                _ => Convert.ToInt32(ins.Operand)
            };
            
            return method.Parameters[index].Name;
        }

        private static int GetLocIndex(Instruction ins)
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

        private static int GetLdcI4Value(Instruction ins)
        {
            return ins.OpCode.Code switch
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
        }
    }
}
