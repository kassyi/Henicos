namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Encapsulates the host-orchestrated Euclidean algorithm loop for modular inverse,
/// isolating the synchronous CPU-GPU control flow from the main advanced math handler.
/// </summary>
internal static class ModInverseHostLoopController
{
    public static void Execute(
        GpuWorkspace workspace,
        GpuBigUInt value,
        GpuBigUInt modulus,
        GpuBigUInt resultBuffer)
    {
        var numWords = value.NumWords;
        using var i = workspace.RentTemp(numWords);
        using var a = workspace.RentTemp(numWords);
        using var v = workspace.RentTemp(numWords);
        using var d = workspace.RentTemp(numWords);
        using var t = workspace.RentTemp(numWords);
        using var rem = workspace.RentTemp(numWords);
        using var tx = workspace.RentTemp(numWords * 2);
        using var modulus2X = workspace.RentTemp(numWords * 2);

        modulus2X.Clear();
        workspace.Provider.CopyDeviceToDevice(modulus2X.DevicePointer, modulus.DevicePointer, numWords,
            workspace.Stream);

        using var txMod2X = workspace.RentTemp(numWords * 2);
        using var txMod = workspace.RentTemp(numWords);
        using var nextD = workspace.RentTemp(numWords);

        workspace.Provider.CopyDeviceToDevice(i.DevicePointer, modulus.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(a.DevicePointer, value.DevicePointer, numWords, workspace.Stream);

        v.Clear();
        d.Clear();
        using var one = workspace.RentTemp(numWords);
        one.Clear();
        workspace.Provider.WriteWord(one.DevicePointer, 0, 1, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(d.DevicePointer, one.DevicePointer, numWords, workspace.Stream);

        while (true)
        {
            if (a.IsZero())
            {
                break;
            }

            GpuBigIntDivRem.DivRem(i, a, t, rem);
            workspace.Provider.CopyDeviceToDevice(i.DevicePointer, a.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(a.DevicePointer, rem.DevicePointer, numWords, workspace.Stream);

            tx.Clear();
            GpuBigIntMath.Mul(t, d, tx);

            txMod2X.Clear();
            GpuBigIntDivRem.Mod(tx, modulus2X, txMod2X);
            workspace.Provider.CopyDeviceToDevice(txMod.DevicePointer, txMod2X.DevicePointer, numWords,
                workspace.Stream);

            var cmp = v.CompareTo(txMod);
            if (cmp >= 0)
            {
                GpuBigIntMath.Sub(v, txMod, nextD);
            }
            else
            {
                GpuBigIntMath.Sub(txMod, v, nextD);
                GpuBigIntMath.Sub(modulus, nextD, nextD);
            }

            workspace.Provider.CopyDeviceToDevice(v.DevicePointer, d.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(d.DevicePointer, nextD.DevicePointer, numWords, workspace.Stream);
        }

        if (i.CompareTo(one) != 0)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is not 1).");
        }

        workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, v.DevicePointer, numWords, workspace.Stream);
    }
}
