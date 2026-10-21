using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class StatementsSnapshotKernels
{
    [GpuKernel]
    public static void BasicStatementsKernel(int[] output, int n)
    {
        int total = 0;
        
        // IfStatement & ReturnStatement
        if (n <= 0)
        {
            return;
        }
        else if (n == 1)
        {
            output[0] = 1;
            return;
        }
        else
        {
            total += 1;
        }
        
        // ForStatement
        for (int i = 0; i < n; i++)
        {
            total += i;
            if (i > 100) break;
            if (i % 2 == 0) continue;
        }
        
        // WhileStatement
        int j = n;
        while (j > 0)
        {
            total *= 2;
            j--;
        }
        
        // DoStatement
        int k = 0;
        do
        {
            total -= 1;
            k++;
        } while (k < 10);
        
        output[0] = total;
    }
}
