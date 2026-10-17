#pragma warning disable SA1600, CS1591

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntVectorKernels
{
    public static partial class AotContexts
    {
        internal interface IVectorBinaryAotContext
        {
            void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar);
        }

        internal interface IVectorFloatBinaryAotContext : IVectorBinaryAotContext
        {
            void InitializeFloatBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr expA,
                IntPtr expB,
                IntPtr expResult,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                int scalarExp,
                uint scalarSign,
                bool isScalar);
        }

        public partial struct VectorAddSub : IVectorBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
                BitOffsetA = 0;
                BitOffsetB = 0;
            }
        }

        public partial struct VectorMul : IVectorBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
            }
        }

        public partial struct VectorDivMod : IVectorBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
                PreShiftBits = 0;
            }
        }

        public partial struct VectorFloatAddSub : IVectorFloatBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar) =>
                InitializeFloatBinary(a, b, result, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, 0, scalarSign, isScalar);

            public void InitializeFloatBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr expA,
                IntPtr expB,
                IntPtr expResult,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                int scalarExp,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                ExpA = expA;
                ExpB = expB;
                ExpResult = expResult;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarExp = scalarExp;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
            }
        }

        public partial struct VectorFloatMul : IVectorFloatBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar) =>
                InitializeFloatBinary(a, b, result, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, 0, scalarSign, isScalar);

            public void InitializeFloatBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr expA,
                IntPtr expB,
                IntPtr expResult,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                int scalarExp,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                ExpA = expA;
                ExpB = expB;
                ExpResult = expResult;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarExp = scalarExp;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
            }
        }

        public partial struct VectorFloatDiv : IVectorFloatBinaryAotContext
        {
            public void InitializeBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                uint scalarSign,
                bool isScalar) =>
                InitializeFloatBinary(a, b, result, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, 0, scalarSign, isScalar);

            public void InitializeFloatBinary(
                IntPtr a,
                IntPtr b,
                IntPtr result,
                IntPtr expA,
                IntPtr expB,
                IntPtr expResult,
                IntPtr signA,
                IntPtr signB,
                IntPtr signResult,
                int numWordsA,
                int numWordsB,
                int numWordsResult,
                int scalarExp,
                uint scalarSign,
                bool isScalar)
            {
                A = a;
                B = b;
                Result = result;
                ExpA = expA;
                ExpB = expB;
                ExpResult = expResult;
                SignA = signA;
                SignB = signB;
                SignResult = signResult;
                NumWordsA = numWordsA;
                NumWordsB = numWordsB;
                NumWordsResult = numWordsResult;
                ScalarExp = scalarExp;
                ScalarSign = scalarSign;
                IsScalar = isScalar;
            }
        }
    }
}
