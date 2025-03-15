using HarmonyLib;
using static Koik.IKAmplifier.IKAmplifierPlugin;


#if VR
using KK.RootMotion;
using KK.RootMotion.FinalIK;
#elif NOVR
using RootMotion;
using RootMotion.FinalIK;
#endif

namespace Koik.IKAmplifier
{
    internal class Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MotionIK), nameof(MotionIK.Calc))]
        public static void MotionIKCalcPostfix(string stateName, MotionIK __instance)
        {
            Instance.OnStateChange(stateName, __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SolverManager), "Start")]
        public static void CharaInitPostfix(SolverManager __instance)
        {
            if (__instance != null && typeof(FullBodyBipedIK) == __instance.GetType())
            {
                Instance.TryAddAmplifier(__instance as FullBodyBipedIK, null);
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), nameof(HSceneProc.ChangeAnimator))]
        public static void HSceneProcChangeAnimatorPostfix()
        {
            Instance.OnAnimatorsChange();
        }
    }
}