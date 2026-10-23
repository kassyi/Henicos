using Kassyi.GSharp.Core;
namespace AotTest {
    public struct UserDataStruct {
        public int FieldA;
    }

    public partial class ExtremelyLongClassNameToTriggerShorteningThresholdInGlobalCppNames_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789 {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void ExtremelyLongMethodNameToTriggerShorteningThresholdInGlobalCppNames_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789(UserDataStruct[] data, int paramX) {
            var localVal = paramX;
        }
    }
}
