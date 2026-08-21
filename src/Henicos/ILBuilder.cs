using System;
using System.Text;
using System.Collections.Generic;

namespace Henicos;

public class ILBuilder
{
    private StringBuilder _sb = new StringBuilder();
    private int _tempCounter = 0;
    private Stack<string> _freeTemps = new Stack<string>();
    
    public string AllocateTemp()
    {
        if (_freeTemps.Count > 0) return _freeTemps.Pop();
        return $"Temp{_tempCounter++}";
    }
    
    public void FreeTemp(string name)
    {
        if (name.StartsWith("Temp")) _freeTemps.Push(name);
    }
    
    public GpuRegister Register(string name) => new GpuRegister(name);
    
    public ILBuilder Emit(string cmd) 
    {
        _sb.AppendLine(cmd);
        return this;
    }
    
    public ILBuilder Assign(GpuRegister dest, GpuExpr expr)
    {
        var src = expr.Compile(this);
        if (src.RegName != dest.Name)
        {
            Emit($"COPY {dest.Name} {src.RegName}");
        }
        if (src.IsTemp) FreeTemp(src.RegName);
        return this;
    }
    
    public ILBuilder LoopE(Action<ILBuilder> body)
    {
        Emit("LOOP_E");
        body(this);
        Emit("END_LOOP");
        return this;
    }

    public ILBuilder LoopEMinus4(Action<ILBuilder> body)
    {
        Emit("LOOP_E_MINUS_4");
        body(this);
        Emit("END_LOOP");
        return this;
    }

    public ILBuilder For(int count, Action<ILBuilder> body)
    {
        for (int i = 0; i < count; i++)
        {
            body(this);
        }
        return this;
    }
    
    public ILBuilder Apply(Action<ILBuilder> action)
    {
        action(this);
        return this;
    }

    public string Build() => _sb.ToString();
}

public class ValueInfo
{
    public string RegName { get; set; } = "";
    public bool IsTemp { get; set; }
}

public abstract class GpuExpr
{
    public abstract ValueInfo Compile(ILBuilder ctx);

    public GpuExpr Square() => new SquareExpr(this);

    public static GpuExpr operator +(GpuExpr a, GpuExpr b) => new BinaryExpr("ADD", a, b);
    public static GpuExpr operator -(GpuExpr a, GpuExpr b) => new BinaryExpr("SUB", a, b);
    public static GpuExpr operator &(GpuExpr a, GpuExpr b) => new BinaryExpr("AND", a, b);
    public static GpuExpr operator <<(GpuExpr a, int bits) => new ShiftExpr("SHL", a, bits);
    public static GpuExpr operator >>(GpuExpr a, int bits) => new ShiftExpr("SHR", a, bits);
}

public class GpuRegister : GpuExpr
{
    public string Name { get; }
    public GpuRegister(string name) { Name = name; }
    public override ValueInfo Compile(ILBuilder ctx) => new ValueInfo { RegName = Name, IsTemp = false };
}

public class BinaryExpr : GpuExpr
{
    public string Op { get; }
    public GpuExpr Left { get; }
    public GpuExpr Right { get; }
    
    public BinaryExpr(string op, GpuExpr left, GpuExpr right)
    {
        Op = op; Left = left; Right = right;
    }
    
    public override ValueInfo Compile(ILBuilder ctx)
    {
        var left = Left.Compile(ctx);
        var right = Right.Compile(ctx);
        string tempReg = ctx.AllocateTemp();
        ctx.Emit($"{Op} {tempReg} {left.RegName} {right.RegName}");
        if (left.IsTemp) ctx.FreeTemp(left.RegName);
        if (right.IsTemp) ctx.FreeTemp(right.RegName);
        return new ValueInfo { RegName = tempReg, IsTemp = true };
    }
}

public class ShiftExpr : GpuExpr
{
    public string Op { get; }
    public GpuExpr Left { get; }
    public int Bits { get; }
    
    public ShiftExpr(string op, GpuExpr left, int bits)
    {
        Op = op; Left = left; Bits = bits;
    }
    
    public override ValueInfo Compile(ILBuilder ctx)
    {
        var left = Left.Compile(ctx);
        string tempReg = ctx.AllocateTemp();
        ctx.Emit($"{Op} {tempReg} {left.RegName} {Bits}");
        if (left.IsTemp) ctx.FreeTemp(left.RegName);
        return new ValueInfo { RegName = tempReg, IsTemp = true };
    }
}

public class SquareExpr : GpuExpr
{
    public GpuExpr Target { get; }
    public SquareExpr(GpuExpr target) { Target = target; }
    
    public override ValueInfo Compile(ILBuilder ctx)
    {
        var tgt = Target.Compile(ctx);
        string tempReg = ctx.AllocateTemp();
        ctx.Emit($"SQUARE {tempReg} {tgt.RegName}");
        if (tgt.IsTemp) ctx.FreeTemp(tgt.RegName);
        return new ValueInfo { RegName = tempReg, IsTemp = true };
    }
}

public static class FermatILGenerator
{
    public static string Generate(int E)
    {
        var il = new ILBuilder();
        int bit2E1 = 2 * E + 1;

        var T = il.Register("T");
        var MaskR = il.Register("MaskR");
        var Q = il.Register("Q");
        var R = il.Register("R");

        void SquareAndReduce(ILBuilder b) => b
            .Assign(T, T.Square())
            .For(3, b2 => b2
                .Assign(Q, T >> bit2E1)
                .Assign(R, T & MaskR)
                .Assign(T, R + (Q << E) - Q - (Q << 1) - (Q << 3))
            );

        void MultiplyBy2AndReduce(ILBuilder b) => b
            .Assign(T, T << 1)
            .Assign(Q, T >> bit2E1)
            .Assign(R, T & MaskR)
            .Assign(T, R + (Q << E) - Q - (Q << 1) - (Q << 3));

        return il
            .LoopE(b => {
                SquareAndReduce(b);
                MultiplyBy2AndReduce(b);
            })
            .LoopEMinus4(b => {
                SquareAndReduce(b);
            })
            // The remaining exponent bits are 1010
            .Apply(SquareAndReduce).Apply(MultiplyBy2AndReduce) // bit 3 (1)
            .Apply(SquareAndReduce)                             // bit 2 (0)
            .Apply(SquareAndReduce).Apply(MultiplyBy2AndReduce) // bit 1 (1)
            .Apply(SquareAndReduce)                             // bit 0 (0)
            .Build();
    }
}
