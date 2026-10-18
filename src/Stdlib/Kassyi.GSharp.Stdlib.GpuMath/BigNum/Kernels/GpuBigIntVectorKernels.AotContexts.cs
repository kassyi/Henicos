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

        public struct VectorBinaryOperation
        {
            public void Initialize(
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
                Left = new SignedVectorOperandReadOnly
                {
                    Limbs = a,
                    Signs = signA,
                    NumWords = numWordsA
                };
                Right = new SignedVectorOperandReadOnly
                {
                    Limbs = b,
                    Signs = signB,
                    NumWords = numWordsB
                };
                Result = new SignedVectorOutput
                {
                    Limbs = result,
                    Signs = signResult,
                    NumWords = numWordsResult
                };
                ScalarSign = scalarSign;
                IsScalar = isScalar;
            }
        }

        public partial struct VectorFloatBinaryOperation
        {
            public void Initialize(
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
                Core.Initialize(
                    a, b, result, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarSign, isScalar);
                Exponents = new FloatVectorExponents
                {
                    ExpA = expA,
                    ExpB = expB,
                    ExpResult = expResult,
                    ScalarExp = scalarExp
                };
            }
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
                Op.Initialize(
                    a, b, result, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarSign, isScalar);
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
                bool isScalar) =>
                Op.Initialize(
                    a, b, result, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarSign, isScalar);
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
                Op.Initialize(
                    a, b, result, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarSign, isScalar);
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
                bool isScalar) =>
                FloatOp.Initialize(
                    a, b, result, expA, expB, expResult, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarExp, scalarSign, isScalar);
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
                bool isScalar) =>
                FloatOp.Initialize(
                    a, b, result, expA, expB, expResult, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarExp, scalarSign, isScalar);
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
                bool isScalar) =>
                FloatOp.Initialize(
                    a, b, result, expA, expB, expResult, signA, signB, signResult,
                    numWordsA, numWordsB, numWordsResult, scalarExp, scalarSign, isScalar);
        }
    }
}
