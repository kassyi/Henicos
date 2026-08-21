using System.Text;
using System.Collections.Generic;

namespace Henicos;

public class GpuILBuilder
{
    private StringBuilder _sb = new StringBuilder();

    public void Square(string dest, string src) => _sb.AppendLine($"SQUARE {dest} {src}");
    public void ShiftLeft(string dest, string src, int bits) => _sb.AppendLine($"SHL {dest} {src} {bits}");
    public void ShiftRight(string dest, string src, int bits) => _sb.AppendLine($"SHR {dest} {src} {bits}");
    public void BitwiseAnd(string dest, string src, string mask) => _sb.AppendLine($"AND {dest} {src} {mask}");
    public void Add(string dest, string a, string b) => _sb.AppendLine($"ADD {dest} {a} {b}");
    public void Sub(string dest, string a, string b) => _sb.AppendLine($"SUB {dest} {a} {b}");
    public void Copy(string dest, string src) => _sb.AppendLine($"COPY {dest} {src}");
    
    // Commands to mark the boundaries of loops
    public void BeginLoopE() => _sb.AppendLine("LOOP_E");
    public void BeginLoopEMinus4() => _sb.AppendLine("LOOP_E_MINUS_4");
    public void EndLoop() => _sb.AppendLine("END_LOOP");

    public override string ToString() => _sb.ToString();
}

public static class FermatILGenerator
{
    public static string Generate(int E)
    {
        var il = new GpuILBuilder();
        int bit2E1 = 2 * E + 1;

        void SquareAndReduce()
        {
            il.Square("T", "T"); 
            for (int pass = 0; pass < 3; pass++)
            {
                il.ShiftRight("Q", "T", bit2E1);
                il.BitwiseAnd("R", "T", "MaskR");
                il.Copy("T", "R");
                il.ShiftLeft("Temp", "Q", E);
                il.Add("T", "T", "Temp");
                il.Sub("T", "T", "Q");
                il.ShiftLeft("Temp", "Q", 1);
                il.Sub("T", "T", "Temp");
                il.ShiftLeft("Temp", "Q", 3);
                il.Sub("T", "T", "Temp");
            }
        }

        void MultiplyBy2AndReduce()
        {
            il.ShiftLeft("Temp", "T", 1);
            il.Copy("T", "Temp");
            il.ShiftRight("Q", "T", bit2E1);
            il.BitwiseAnd("R", "T", "MaskR");
            il.Copy("T", "R");
            il.ShiftLeft("Temp", "Q", E);
            il.Add("T", "T", "Temp");
            il.Sub("T", "T", "Q");
            il.ShiftLeft("Temp", "Q", 1);
            il.Sub("T", "T", "Temp");
            il.ShiftLeft("Temp", "Q", 3);
            il.Sub("T", "T", "Temp");
        }

        il.BeginLoopE();
        SquareAndReduce();
        MultiplyBy2AndReduce();
        il.EndLoop();

        il.BeginLoopEMinus4();
        SquareAndReduce();
        il.EndLoop();

        SquareAndReduce(); MultiplyBy2AndReduce(); // bit 3 (1)
        SquareAndReduce(); // bit 2 (0)
        SquareAndReduce(); MultiplyBy2AndReduce(); // bit 1 (1)
        SquareAndReduce(); // bit 0 (0)

        return il.ToString();
    }
}
