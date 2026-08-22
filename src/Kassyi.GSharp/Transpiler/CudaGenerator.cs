using System.Security.Cryptography;
using Kassyi.GSharp.Transpiler.Pipeline;

namespace Kassyi.GSharp.Transpiler;

public class CudaGenerator : IAstVisitor
{
    private readonly CodeWriter _writer = new();

    /// <summary>
    /// Pipeline Step 3: Generates CUDA C++ code from the AST model.
    /// </summary>
    public static TranspiledSource Generate(AstContext context)
    {
        if (context.Metadata == null || context.RootBlock == null)
        {
            throw new ArgumentException("AstContext is incomplete. Must have Metadata and RootBlock.");
        }

        var generator = new CudaGenerator();
        var cudaCode = generator.Generate(context.RootBlock, context.LocalVariables);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(cudaCode));
        var hashString = Convert.ToHexStringLower(hashBytes);

        return new TranspiledSource(
            Metadata: context.Metadata,
            CudaCode: cudaCode,
            SourceHash: hashString
        );
    }

    public string Generate(BlockNode root, Dictionary<string, TypeReference>? variables)
    {
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                var typeStr = CudaTypeMapper.MapType(kvp.Value);
                _writer.WriteLine($"{typeStr} {kvp.Key};");
            }
        }

        Visit(root);
        return _writer.ToString();
    }

    public void Visit(BlockNode node)
    {
        foreach (var stmt in node.Statements)
        {
            stmt.Accept(this);
        }
    }

    public void Visit(ForLoopNode node)
    {
        _writer.Write("for ");
        using (_writer.Parens())
        {
            _writer.Write("int ").Write(node.IteratorName).Write(" = ");
            node.Start.Accept(this);
            _writer.Write("; ").Write(node.IteratorName).Write(" < ");
            node.End.Accept(this);
            _writer.Write("; ").Write(node.IteratorName).Write("++");
        }

        using (_writer.Block())
        {
            Visit(node.Body);
        }
    }

    public void Visit(WhileNode node)
    {
        _writer.Write("while ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }

        using (_writer.Block())
        {
            Visit(node.Body);
        }
    }

    public void Visit(IfNode node)
    {
        _writer.Write("if ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }

        using (_writer.Block())
        {
            Visit(node.TrueBlock);
        }

        if (node.FalseBlock == null)
        {
            return;
        }

        _writer.Write("else");
        using (_writer.Block())
        {
            Visit(node.FalseBlock);
        }
    }

    public void Visit(AssignNode node)
    {
        _writer.Write(node.TargetName).Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    public void Visit(ArrayAssignNode node)
    {
        _writer.Write(node.ArrayName);
        using (_writer.Brackets())
        {
            node.Index.Accept(this);
        }

        _writer.Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    private static readonly FrozenDictionary<BinaryOperator, string> s_binOpStrs =
        new Dictionary<BinaryOperator, string>
        {
            { BinaryOperator.Add, " + " },
            { BinaryOperator.Sub, " - " },
            { BinaryOperator.Mul, " * " },
            { BinaryOperator.Div, " / " },
            { BinaryOperator.Mod, " % " },
            { BinaryOperator.BitAnd, " & " },
            { BinaryOperator.BitOr, " | " },
            { BinaryOperator.BitXor, " ^ " },
            { BinaryOperator.ShiftLeft, " << " },
            { BinaryOperator.ShiftRight, " >> " },
            { BinaryOperator.Eq, " == " },
            { BinaryOperator.Neq, " != " },
            { BinaryOperator.Lt, " < " },
            { BinaryOperator.Lte, " <= " },
            { BinaryOperator.Gt, " > " },
            { BinaryOperator.Gte, " >= " }
        }.ToFrozenDictionary();

    public void Visit(BinaryOpNode node)
    {
        using (_writer.Parens())
        {
            node.Left.Accept(this);

            if (!s_binOpStrs.TryGetValue(node.Op, out var opStr))
            {
                throw new NotImplementedException();
            }

            _writer.Write(opStr);
            node.Right.Accept(this);
        }
    }

    public void Visit(LiteralNode node) => _writer.Write(node.Value);

    public void Visit(VariableNode node) => _writer.Write(node.Name);

    public void Visit(ArrayAccessNode node)
    {
        _writer.Write(node.ArrayName);
        using (_writer.Brackets())
        {
            node.Index.Accept(this);
        }
    }

    public void Visit(MethodCallNode node)
    {
        _writer.Write(node.MethodName);
        using (_writer.Parens())
        {
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                node.Arguments[i].Accept(this);
            }
        }
    }

    public void Visit(LabelNode node) => _writer.WriteLine($"{node.Name}:");

    public void Visit(GotoNode node) => _writer.WriteLine($"goto {node.TargetName};");

    public void Visit(IfGotoNode node)
    {
        _writer.Write("if ");
        using (_writer.Parens())
        {
            node.Condition.Accept(this);
        }

        _writer.WriteLine($" goto {node.TrueTargetName};");
    }

    public void Visit(SwitchNode node)
    {
        _writer.Write("switch ");
        using (_writer.Parens())
        {
            node.Value.Accept(this);
        }

        using (_writer.Block())
        {
            for (var i = 0; i < node.CaseLabels.Length; i++)
            {
                _writer.WriteLine($"case {i}: goto {node.CaseLabels[i]};");
            }
        }
    }

    public void Visit(ReturnNode node)
    {
        if (node.Value != null)
        {
            _writer.Write("return ");
            node.Value.Accept(this);
            _writer.WriteLine(";");
        }
        else
        {
            _writer.WriteLine("return;");
        }
    }

    public void Visit(AddressOfNode node)
    {
        _writer.Write("&");
        using (_writer.Parens())
        {
            node.Target.Accept(this);
        }
    }

    public void Visit(DereferenceNode node)
    {
        _writer.Write("*");
        using (_writer.Parens())
        {
            node.Pointer.Accept(this);
        }
    }

    public void Visit(DereferenceAssignNode node)
    {
        _writer.Write("*");
        using (_writer.Parens())
        {
            node.Pointer.Accept(this);
        }

        _writer.Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    public void Visit(ExpressionStatementNode node)
    {
        node.Expression.Accept(this);
        _writer.WriteLine(";");
    }

    public void Visit(FieldAccessNode node)
    {
        node.Target.Accept(this);
        _writer.Write(node.IsPointer ? "->" : ".");
        _writer.Write(node.FieldName);
    }

    public void Visit(FieldAssignNode node)
    {
        node.Target.Accept(this);
        _writer.Write(node.IsPointer ? "->" : ".");
        _writer.Write(node.FieldName);
        _writer.Write(" = ");
        node.Value.Accept(this);
        _writer.WriteLine(";");
    }

    public void Visit(FieldAddressNode node)
    {
        _writer.Write("&");
        using (_writer.Parens())
        {
            node.Target.Accept(this);
            _writer.Write(node.IsPointer ? "->" : ".");
            _writer.Write(node.FieldName);
        }
    }

    public void Visit(IntrinsicNode node)
    {
        switch (node.Intrinsic)
        {
            case IntrinsicType.ThreadIdxX: _writer.Write("threadIdx.x"); break;
            case IntrinsicType.ThreadIdxY: _writer.Write("threadIdx.y"); break;
            case IntrinsicType.ThreadIdxZ: _writer.Write("threadIdx.z"); break;

            case IntrinsicType.BlockIdxX: _writer.Write("blockIdx.x"); break;
            case IntrinsicType.BlockIdxY: _writer.Write("blockIdx.y"); break;
            case IntrinsicType.BlockIdxZ: _writer.Write("blockIdx.z"); break;

            case IntrinsicType.BlockDimX: _writer.Write("blockDim.x"); break;
            case IntrinsicType.BlockDimY: _writer.Write("blockDim.y"); break;
            case IntrinsicType.BlockDimZ: _writer.Write("blockDim.z"); break;

            case IntrinsicType.GridDimX: _writer.Write("gridDim.x"); break;
            case IntrinsicType.GridDimY: _writer.Write("gridDim.y"); break;
            case IntrinsicType.GridDimZ: _writer.Write("gridDim.z"); break;

            case IntrinsicType.SyncThreads:
                _writer.Write("__syncthreads()");
                break;

            case IntrinsicType.AtomicAdd:
                _writer.Write("atomicAdd");
                using (_writer.Parens())
                {
                    node.Arguments[0].Accept(this);
                    _writer.Write(", ");
                    node.Arguments[1].Accept(this);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(node.Intrinsic), node.Intrinsic, null);
        }
    }
}
