using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Transpiler;

public class GEN_35_MacroTranspiler_InGpuKernel
{
    [GpuKernel(Cooperative = true)]
    public static void MyKernel()
    {
        int sum = 0;
        
        // ForUnroll macro
        Gpu.ForUnroll(0, 3, i => 
        {
            sum += i;
        });
        
        // GridStrideLoop macro
        Gpu.GridStrideLoop(10, j => 
        {
            sum += j;
        });
        
        // SingleThread macro
        Gpu.SingleThread(() => 
        {
            sum += 100;
        });
        
        // SyncGrid macro - Edge case 1: Empty block
        Gpu.SyncGrid(() =>
        {
        });

        // SyncGrid macro - Edge case 2: Single statement block
        Gpu.SyncGrid(() =>
        {
            sum += 1;
        });

        // SyncGrid macro - Edge case 3: Expression body lambda
        Gpu.SyncGrid(() => sum += 2);

        // SyncGrid macro - Edge case 4: Single unbraced statement in if
        Gpu.SyncGrid(() =>
        {
            if (sum > 0) sum += 10;
            sum += 20;
        });

        // SyncGrid macro - Comprehensive steps with nested for loop and if-else
        Gpu.SyncGrid(() =>
        {
            sum += 1;
            sum += 2;
            for (int i = 0; i < 10; i++)
            {
                sum += i;
                sum *= 2;
            }
            if (sum > 50)
            {
                sum += 100;
                sum += 200;
            }
            else
            {
                sum -= 10;
                sum -= 20;
            }
            sum += 3;
            Gpu.SyncGrid(); // Manual sync co-existence (should not produce double sync)
            sum += 4;
        });

        // SyncGrid macro - Deep nesting of nesting (for -> if -> for)
        Gpu.SyncGrid(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                if (i % 2 == 0)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        sum += i;
                        sum += j;
                    }
                    sum += 10;
                }
                sum += 20;
            }
        });

        // SyncGrid macro - Recursive / nested SyncGrid inside SyncGrid
        Gpu.SyncGrid(() =>
        {
            sum += 1;
            Gpu.SyncGrid(() =>
            {
                sum += 2;
                sum += 3;
            });
            sum += 4;
        });

        // SyncGrid macro - Direct nested SyncGrid wrapper pattern (inner SyncGrid is unwrapped/eliminated)
        Gpu.SyncGrid(() =>
        {
            Gpu.SyncGrid(() =>
            {
                sum += 500;
            });
        });

        // SyncGrid macro - Arrow function (lambda) and local function inside
        Gpu.SyncGrid(() =>
        {
            void LocalStep()
            {
                sum += 11;
                sum += 22;
            }

            LocalStep();
            sum += 33;
        });

        // SyncGrid macro - SingleThread inside SyncGrid (must suppress sync to avoid GPU deadlock)
        Gpu.SyncGrid(() =>
        {
            sum += 1;
            Gpu.SingleThread(() =>
            {
                sum += 2;
                sum += 3;
            });
            sum += 4;
        });
    }
}
