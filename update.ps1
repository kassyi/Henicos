$file = '.\src\Kassyi.GSharp.GpuBigInt\GpuBigIntFermatKernels.cs'
$lines = Get-Content $file

$before = $lines[0..637]
$after = $lines[764..($lines.Length-1)]

$newContent = @'
    [GpuDevice, GpuInline]
    public static void Device_MultiplySquareAndReduce(
        int reduceCount,
        uint[] t, uint[] q, uint[] r, uint[] maskR,
        uint[] tTmp,
        int numWords, int e, int bit2E1, long[] carryBuffer,
        int m, uint[] nttBuf1, uint[] nttBuf2, uint[] nttTmp,
        uint[] twiddle1Fwd, uint[] twiddle2Fwd,
        uint[] twiddle1Inv, uint[] twiddle2Inv)
    {
        Device_MultiplySquare(t, numWords, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(t, q, r, maskR, tTmp, numWords * 2 + 16, e, bit2E1, carryBuffer);
        }
    }

    [GpuDevice, GpuInline]
    public static void Device_ShiftLeftAndReduce(
        int reduceCount,
        uint[] t, uint[] q, uint[] r, uint[] maskR, uint[] tTmp,
        int numWordsExtended, int e, int bit2E1, long[] carryBuffer)
    {
        Device_ShiftLeft(t, 1, tTmp, numWordsExtended);
        Device_Copy(tTmp, t, numWordsExtended);
        for (var j = 0; j < reduceCount; j++)
        {
            Device_FermatReduceStep(t, q, r, maskR, tTmp, numWordsExtended, e, bit2E1, carryBuffer);
        }
    }

    [GpuKernel(Cooperative = true)]
    public static void Kernel_StaticFermatLoop(
        uint[] t, uint[] q, uint[] r, uint[] maskR,
        uint[] tTmp,
        int numWords, int e, long[] carryBuffer,
        int m, uint[] nttBuf1, uint[] nttBuf2, uint[] nttTmp,
        uint[] twiddle1Fwd, uint[] twiddle2Fwd,
        uint[] twiddle1Inv, uint[] twiddle2Inv)
    {
        var bit2E1 = 2 * e + 1;
        var numWordsExtended = numWords * 2 + 16;

        for (var iter = 0; iter < e; iter++)
        {
            Device_MultiplySquareAndReduce(3, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
            Device_ShiftLeftAndReduce(3, t, q, r, maskR, tTmp, numWordsExtended, e, bit2E1, carryBuffer);
        }

        for (var iter = 0; iter < e - 4; iter++)
        {
            Device_MultiplySquareAndReduce(3, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        Device_ShiftLeftAndReduce(1, t, q, r, maskR, tTmp, numWordsExtended, e, bit2E1, carryBuffer);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);

        for (var iter = 0; iter < e - 4; iter++)
        {
            Device_MultiplySquareAndReduce(3, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        }

        // bit 3 (1)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        Device_ShiftLeftAndReduce(1, t, q, r, maskR, tTmp, numWordsExtended, e, bit2E1, carryBuffer);

        // bit 2 (0)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);

        // bit 1 (1)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
        Device_ShiftLeftAndReduce(1, t, q, r, maskR, tTmp, numWordsExtended, e, bit2E1, carryBuffer);

        // bit 0 (0)
        Device_MultiplySquareAndReduce(9, t, q, r, maskR, tTmp, numWords, e, bit2E1, carryBuffer, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
    }
'@

Set-Content -Path $file -Value ($before + $newContent -split "
" + $after)
