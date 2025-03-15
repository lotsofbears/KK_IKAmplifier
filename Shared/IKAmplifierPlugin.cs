using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using Amplifier = Koik.IKAmplifier.Amplifier;
using EffectorLink = Koik.IKAmplifier.Amplifier.Body.EffectorLink;
using File = System.IO.File;
using KKAPI.Utilities;

using System.Collections;
using BepInEx.Configuration;
using System.Reflection;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;
using Illusion.Extensions;
using UnityEngine.SceneManagement;
using static Illusion.Utils;




#if VR
using KK.RootMotion;
using KK.RootMotion.FinalIK;
#elif NOVR
using RootMotion;
using RootMotion.FinalIK;
#endif

namespace Koik.IKAmplifier
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInProcess(KoikatuAPI.GameProcessName)]
#if KK
    [BepInProcess(KoikatuAPI.GameProcessNameSteam)]
#endif
    [BepInDependency(KoikatuAPI.GUID)]
    public class IKAmplifierPlugin : BaseUnityPlugin
    {
        private class AmpDefs(EffectorLink[] effectorLinks, float speed, string transform)
        {
            internal readonly EffectorLink[] effectorLinks = effectorLinks;
            internal readonly float speed = speed;
            internal readonly string transform = transform;
            //internal readonly int[] effectors;
            //internal readonly float[] weights;
        }
        private class AnimParam(float body, float shoulder, float thigh, float hand, float foot)
        {
            internal readonly float body = body;
            internal readonly float shoulder = shoulder;
            internal readonly float thigh = thigh;
            internal readonly float hand = hand;
            internal readonly float foot = foot;
        }
#if VR
#if KK
        public const string Name = "KK_IKAmplifier.VR";
        public const string GUID = "kk.ik.amplifier.vr";
#elif KKS
        public const string Name = "KKS_IKAmplifier.VR";
        public const string GUID = "kks.ik.amplifier.vr";
#endif

#elif NOVR
#if KK
        public const string Name = "KK_IKAmplifier";
        public const string GUID = "kk.ik.amplifier";
#elif KKS
        public const string Name = "KKS_IKAmplifier";
        public const string GUID = "kks.ik.amplifier";
#endif
#endif
        public const string Version = "1.0.0";

        public new static ManualLogSource Logger;

        private readonly Dictionary<string, AmpDefs> _amplifierDefinitionDic = [];

        private Harmony _patch;
        public static IKAmplifierPlugin Instance { get; private set; }

        private readonly Dictionary<ChaControl, Amplifier> _amplifierDic = [];

        private readonly Dictionary<string, AnimParam> _animParamDic = [];
        //private AnimParam _currentAnimParam;
        //private readonly AnimParam _defaultAnimParam = new(1f, 1f, 1f, 0f, 0f);
        private AnimParam _defaultAnimParamMale;
        private AnimParam _defaultAnimParamFemale;

        #region Config

        public static ConfigEntry<bool> ConfigEnable { get; set; }
        public static ConfigEntry<float> ConfigSpeedGlobal { get; set; }
        public static ConfigEntry<float> ConfigWeightGlobal { get; set; }
        public static ConfigEntry<float> ConfigWeightWeak { get; set; }
        public static ConfigEntry<float> ConfigWeightStrong { get; set; }
        public static ConfigEntry<float> ConfigWeightMale { get; set; }
        public static ConfigEntry<float> ConfigWeightFemale { get; set; }
#if NOVR
        public static ConfigEntry<bool> ConfigWeirdTransition { get; set; }
#endif
        public static ConfigEntry<bool> ConfigRunOutsideH {  get; set; }

#endregion


        private void Start()
        {
            StartCoroutine(InitCo());
        }
        private IEnumerator InitCo()
        {
#if KK
            yield return new WaitUntil(() => KKAPI.SceneApi.GetLoadSceneName().Equals("Title"));
#elif KKS
            // KKAPI gives us crash this early in KKS.
            yield return new WaitUntil(() => Manager.Scene.initialized && Manager.Scene.LoadSceneName.Equals("Title"));
#endif
            Instance = this;
            Logger = base.Logger;
#if VR
            if (!IsVR())
            {
                LogInfo($"No VR detected, the plugin is fully disabled.");
                Destroy(this);
                yield break;
            }

#elif NOVR
            if (IsVR())
            {
                LogInfo($"Detected VR, the plugin is fully disabled.");
                Destroy(this);
                yield break;
            }
#endif
            AddDefinitionDic();
            AddConfig();
            ReadCSV();
            TryEnable();
            SceneManager.sceneUnloaded += OnSceneUnloaded;

        }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_amplifierDic == null) return;

            foreach (var kv in _amplifierDic)
            {
                if (kv.Key == null || kv.Value == null)
                {
                    _amplifierDic.Clear();
                    break;
                }
            }
        }
        private void TryEnable()
        {
            if (ConfigEnable.Value)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(Patches));

                if (KKAPI.MainGame.GameAPI.InsideHScene)
                {
                    foreach (var chara in FindObjectsOfType<ChaControl>())
                    {
                        var fbbik = chara.objAnim.GetComponent<FullBodyBipedIK>();
                        if (fbbik == null) continue;

                        TryAddAmplifier(fbbik, chara);
                    }
                }
            }
            else
            {
                _patch?.UnpatchSelf();

                foreach (var amp in _amplifierDic.Values)
                {
                    Destroy(amp);
                }
                _amplifierDic.Clear();
            }
        }


        private bool IsVR()
        {
#if KK
            var type = AccessTools.TypeByName("SteamVR");
#elif KKS
            var type = AccessTools.TypeByName("Valve.VR.SteamVR");
#endif
            if (type != null)
            {
                var property = AccessTools.Property(type, "active");
                if (property != null)
                {
                    return (bool)property.GetValue(null, null);
                }
            }
            return false;
        }

        private void AddConfig()
        {
            ConfigEnable = Config.Bind("", "Enable", true,
                new ConfigDescription(
                    "State of the plugin. Changes take place immediately",
                    null,
                    new ConfigurationManagerAttributes { Order = 100 }));

            ConfigWeightGlobal = Config.Bind("", "Global Weight", 1f,
                new ConfigDescription(
                    "How much animation is impacted, 1 for full, 0 for none",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 90 }));

            ConfigSpeedGlobal = Config.Bind("", "Global Speed", 1f,
                new ConfigDescription(
                    "How \"far\" amplifier can go",
                    new AcceptableValueRange<float>(0.1f, 5f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 80 }));

#if NOVR
            ConfigWeirdTransition = Config.Bind("", "Transition", false,
                new ConfigDescription(
                    "Add weird transition when animator (position) changes",
                    null,
                    new ConfigurationManagerAttributes { Order = 70 }));
#endif
            ConfigRunOutsideH = Config.Bind("", "Run outside H", false,
                new ConfigDescription(
                    "Add amplifier whenever IK is active.",
                    null,
                    new ConfigurationManagerAttributes { Order = 60 }));


            ConfigWeightWeak = Config.Bind("Weight by Animation Type", "Weak", 1f,
                new ConfigDescription(
                    "",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 100 }));

            ConfigWeightStrong = Config.Bind("Weight by Animation Type", "Strong", 0.6f,
                new ConfigDescription(
                    "",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 90 }));


            ConfigWeightMale = Config.Bind("Weight by Gender", "Male", 1f,
                new ConfigDescription(
                    "",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 100 }));

            ConfigWeightFemale = Config.Bind("Weight by Gender", "Female", 1f,
                new ConfigDescription(
                    "",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = false, Order = 90 }));


            ConfigEnable.SettingChanged += (_, _1) => TryEnable();
            ConfigWeightGlobal.SettingChanged += (_, _1) => UpdateWeight();
            ConfigWeightMale.SettingChanged += (_, _1) => UpdateWeight();
            ConfigWeightFemale.SettingChanged += (_, _1) => UpdateWeight();
            ConfigSpeedGlobal.SettingChanged += (_, _1) => UpdateSpeed();
        }

        private void UpdateWeight()
        {
            foreach (var kv in _amplifierDic)
            {
                if (kv.Value == null) continue;

                kv.Value.Weight = ConfigWeightGlobal.Value * (kv.Key.sex == 0 ? ConfigWeightMale.Value : ConfigWeightFemale.Value);
            }
        }
        private void UpdateSpeed()
        {
            foreach (var amplifier in _amplifierDic.Values)
            {
                if (amplifier == null) continue;

                for (var i = 0; i < amplifier.bodies.Length; i++)
                {
                    amplifier.bodies[i].speed = Mathf.Clamp(_amplifierDefinitionDic.ElementAt(i).Value.speed * ConfigSpeedGlobal.Value, 0f, 10f);
                }
            }
        }
#if DEBUG
        public void TestHand(float weight)
        {
            foreach (var amp in _amplifierDic.Values)
            {
                foreach (var body in amp.bodies)
                {
                    foreach (var link in body.effectorLinks)
                    {
                        if (link.effector == FullBodyBipedEffector.LeftHand || link.effector == FullBodyBipedEffector.RightHand)
                        {
                            link.weight = weight;
                        }
                    }
                }
            }
        }
        public void TestFoot(float weight)
        {
            foreach (var amp in _amplifierDic.Values)
            {
                foreach (var body in amp.bodies)
                {
                    foreach (var link in body.effectorLinks)
                    {
                        if (link.effector == FullBodyBipedEffector.LeftFoot || link.effector == FullBodyBipedEffector.RightFoot)
                        {
                            link.weight = weight;
                        }
                    }
                }
            }
        }
        public void TestShoulder(float weight)
        {
            foreach (var amp in _amplifierDic.Values)
            {
                foreach (var body in amp.bodies)
                {
                    foreach (var link in body.effectorLinks)
                    {
                        if (link.effector == FullBodyBipedEffector.LeftShoulder || link.effector == FullBodyBipedEffector.RightShoulder)
                        {
                            link.weight = weight;
                        }
                    }
                }
            }
        }
        public void TestThigh(float weight)
        {
            foreach (var amp in _amplifierDic.Values)
            {
                foreach (var body in amp.bodies)
                {
                    foreach (var link in body.effectorLinks)
                    {
                        if (link.effector == FullBodyBipedEffector.LeftThigh || link.effector == FullBodyBipedEffector.RightThigh)
                        {
                            link.weight = weight;
                        }
                    }
                }
            }
        }
#endif
        private void AddDefinitionDic()
        {
            _amplifierDefinitionDic.Add("cf_j_spine01", new AmpDefs(
                [
                    new(FullBodyBipedEffector.Body, 1f),
                    //new(FullBodyBipedEffector.LeftHand, 0.5f),
                    //new(FullBodyBipedEffector.RightHand, 0.5f)
                ], 1f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01"));

            _amplifierDefinitionDic.Add("cf_j_arm00_L", new AmpDefs(
                [
                    new(FullBodyBipedEffector.LeftShoulder, 1f),
                    new(FullBodyBipedEffector.LeftHand, 1f)
                ], 1f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_shoulder_L/cf_j_shoulder_L/cf_j_arm00_L"));

            _amplifierDefinitionDic.Add("cf_j_arm00_R", new AmpDefs(
                [
                    new(FullBodyBipedEffector.RightShoulder, 1f),
                    new(FullBodyBipedEffector.RightHand, 1f)
                ], 1f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_shoulder_R/cf_j_shoulder_R/cf_j_arm00_R"));

            _amplifierDefinitionDic.Add("cf_j_thigh00_L", new AmpDefs(
                [
                    new(FullBodyBipedEffector.LeftThigh, 1f),
                    new(FullBodyBipedEffector.LeftFoot, 1f)
                ], 1f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L"));

            _amplifierDefinitionDic.Add("cf_j_thigh00_R", new AmpDefs(
                [
                    new(FullBodyBipedEffector.RightThigh, 1f),
                    new(FullBodyBipedEffector.RightFoot, 1f)
                ], 1f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R"));

            _amplifierDefinitionDic.Add("cf_j_leg03_L", new AmpDefs(
                [
                    new(FullBodyBipedEffector.LeftFoot, 1f)
                ], 3f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L/cf_j_leg01_L/cf_j_leg03_L"));

            _amplifierDefinitionDic.Add("cf_j_leg03_R", new AmpDefs(
                [
                    new(FullBodyBipedEffector.RightFoot, 1f)
                ], 3f, "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R/cf_j_leg01_R/cf_j_leg03_R"));
        }
        internal static void LogDebug(object data) => Logger.LogDebug(data);
        internal static void LogInfo(object data) => Logger.LogInfo(data);

        internal void OnStateChange(string stateName, MotionIK motionIK)
        {
            if (motionIK == null || motionIK.ik == null || motionIK.info == null) return;

            var chara = motionIK.info;

            if (_amplifierDic.ContainsKey(chara))
            {
                if (stateName.StartsWith("S", StringComparison.Ordinal) 
                    || stateName.StartsWith("O", StringComparison.Ordinal))
                {
                    _amplifierDic[chara].Weight = ConfigWeightGlobal.Value * ConfigWeightStrong.Value * (chara.sex == 0 ? ConfigWeightMale.Value : ConfigWeightFemale.Value);
                }
                else if (chara.sex == 0 && stateName.Equals("Insert"))
                {
                    _amplifierDic[chara].Weight = ConfigWeightGlobal.Value * ConfigWeightMale.Value * 0.25f;
                }
                else
                {
                    _amplifierDic[chara].Weight = ConfigWeightGlobal.Value * ConfigWeightWeak.Value * (chara.sex == 0 ? ConfigWeightMale.Value : ConfigWeightFemale.Value);
                }
#if DEBUG
                LogDebug($"OnStateChange:{stateName}:{motionIK.info}:{_amplifierDic[chara].Weight}");
#endif
            }

        }
        private void ReadCSV()
        {
            var execLoc = Assembly.GetExecutingAssembly().Location;
            var folder = execLoc.Substring(0, execLoc.LastIndexOf('\\') + 1);
            var filePath = Path.Combine(folder, "KK_IKAmplifier.csv");

            if (File.Exists(filePath))
            {
                var skipFirstLine = true;
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (skipFirstLine || line.IsNullOrEmpty())
                    {
                        skipFirstLine = false;
                        continue;
                    }

                    // CSV file structure
                    // Description,Name,Female_Body,Female_Shoulder,Female_Thigh,Female_Hand,Female_Foot,Male_Body,Male_Shoulder,Male_Thigh,Male_Hand,Male_Foot
                    // Missionary,khs_f_00,1,1,1,1,1,1,1,1,0,0

                    var array = line.Split(',');

                    if (array.Length < 12)
                    {
#if DEBUG
                        LogDebug($"Bad line in {filePath} - {line}");
#endif
                        continue;
                    }
                    if (array[1].IsNullOrEmpty())
                    {
                        if (_defaultAnimParamMale == null && array[0].StartsWith("Default", StringComparison.OrdinalIgnoreCase))
                        {
                            _defaultAnimParamMale = ParseLine(array, male: true);
                            _defaultAnimParamFemale = ParseLine(array, male: false);
                        }
                        continue;
                    }

                    // Aibu animations, males don't have them.
                    if (!array[1].StartsWith("kha", StringComparison.OrdinalIgnoreCase))
                    {
                        var animNameMale = array[1].Replace("_f_", "_m_");

                        if (!_animParamDic.ContainsKey(animNameMale))
                        {
#if DEBUG
                            LogDebug($"ReadCSV:AddAnimation:{animNameMale}");
#endif
                            _animParamDic.Add(animNameMale, ParseLine(array, true));
                        }
                        else
                        {
                            LogInfo($"Animation name - {array[1]} appears multiple times, but one is sufficient (\"_m_\" or \"_f_\" for male or female)");
                        }
                    }



                    var animNameFemale = array[1].Replace("_m_", "_f_");

                    if (!_animParamDic.ContainsKey(animNameFemale))
                    {
#if DEBUG
                        LogDebug($"ReadCSV:AddAnimation:{animNameFemale}");
#endif
                        _animParamDic.Add(animNameFemale, ParseLine(array, false));
                    }
                    else
                    {
                        LogInfo($"Animation name - {array[1]} appears multiple times, but one is sufficient (\"_m_\" or \"_f_\" for male or female)");
                    }
                }

                // If default line was deleted from csv file.
                if (_defaultAnimParamMale == null)
                {
                    _defaultAnimParamMale = new(1f, 1f, 1f, 0f, 0f);
                    _defaultAnimParamFemale = new(1f, 1f, 1f, 0f, 0f);
                }
            }
            else
            {
                LogInfo($"Couldn't find CSV file with animation parameters, falling back to default.");
            }
        }
        //private string FormatAnimName(string name) => name.Replace("_m_", "_.*_").Replace("_f_", "_.*_");
        private AnimParam ParseLine(string[] array, bool male)
        {
            var number = male ? 5 : 0;
            return new AnimParam(
                float.TryParse(array[2 + number], out var body) ? Mathf.Clamp01(body) : 1,
                float.TryParse(array[3 + number], out var shoulder) ? Mathf.Clamp01(shoulder) : 1,
                float.TryParse(array[4 + number], out var thigh) ? Mathf.Clamp01(thigh) : 1,
                float.TryParse(array[5 + number], out var hand) ? Mathf.Clamp01(hand) : 0,
                float.TryParse(array[6 + number], out var foot) ? Mathf.Clamp01(foot) : 0
                );
        }


        private void OnAnimatorChange(ChaControl chara, Amplifier amplifier)
        {
#if DEBUG
            LogDebug($"OnAnimatorChange:{chara}");
#endif
            if (chara == null || amplifier == null) return;
#if VR
            amplifier.enabled = false;
#elif NOVR
            amplifier.enabled = ConfigWeirdTransition.Value;
#endif
            RunAfterUpdate(amplifier.UpdatePositions, true, 2);

            // Pick animation name from chara.
            var animName = (chara.animBody != null && chara.animBody.runtimeAnimatorController != null) ? chara.animBody.runtimeAnimatorController.name : null;

            if (animName == null || !_animParamDic.TryGetValue(animName, out var animParam))
            {
                animParam = chara.sex == 0 ? _defaultAnimParamMale : _defaultAnimParamFemale;
            }

            foreach (var body in amplifier.bodies)
            {
                var defaultBody = _amplifierDefinitionDic.ContainsKey(body.transform.name) ? _amplifierDefinitionDic[body.transform.name] : null;

                for (int i = 0; i < body.effectorLinks.Length; i++)
                {
                    var link = body.effectorLinks[i];

                    link.weight = defaultBody == null ? 1f : defaultBody.effectorLinks[i].weight;

                    switch (link.effector)
                    {
                        case FullBodyBipedEffector.Body:
                            link.weight *= animParam.body;
                            break;
                        case FullBodyBipedEffector.LeftShoulder:
                        case FullBodyBipedEffector.RightShoulder:
                            link.weight *= animParam.shoulder;
                            break;
                        case FullBodyBipedEffector.LeftThigh:
                        case FullBodyBipedEffector.RightThigh:
                            link.weight *= animParam.thigh;
                            break;
                        case FullBodyBipedEffector.LeftHand:
                        case FullBodyBipedEffector.RightHand:
                            link.weight *= animParam.hand;
                            break;
                        case FullBodyBipedEffector.LeftFoot:
                        case FullBodyBipedEffector.RightFoot:
                            link.weight *= animParam.foot;
                            break;
                    }
                }
            }
        }
        internal void OnAnimatorsChange()
        {
            foreach (var kv in _amplifierDic)
            {
                OnAnimatorChange(kv.Key, kv.Value);
            } 

            // Set delegate to do dick-aim calculations once more since by default they are done before IK solver.
            // VR has near identical setup in VRPlugin.
#if NOVR
            var dans = FindObjectsOfType<Lookat_dan>()
                .Where(t => t.male != null);

            if (!dans.Any()) return;

            var method = AccessTools.FirstMethod(typeof(Lookat_dan), m => m.Name.Equals("LateUpdate"));

            if (method != null)
            {
                foreach (var dan in dans)
                {
                    var ik = dan.male.objAnim.GetComponent<FullBodyBipedIK>();

                    if (ik == null) return;

                    if (ik.solver.OnPostUpdate != null)
                    {
                        var actions = ik.solver.OnPostUpdate.GetInvocationList();
                        if (actions.Any(d => d.Method.Name.StartsWith("<OnAnimatorChange>_", StringComparison.Ordinal)))
                        {
                            continue;
                        }
                    }
                    
#if DEBUG
                    LogDebug($"OnAnimatorChange:AddLookDan");
#endif
                    var methodDelegate = AccessTools.MethodDelegate<Action>(method, dan);
                    ik.solver.OnPostUpdate += () => methodDelegate();
                }
            }
#endif

        }

        private static void RunAfterUpdate(Action action, bool onEndFrame = false, int numberOfUpdates = 1)
        {
            Instance.StartCoroutine(RunAfterUpdateCo(action, onEndFrame, numberOfUpdates));
        }

        private static IEnumerator RunAfterUpdateCo(Action action, bool onEndFrame, int numberOfUpdates)
        {
            var ret = onEndFrame ? CoroutineUtils.WaitForEndOfFrame : null;
            while (numberOfUpdates-- > 0)
            {
                yield return ret;
            }
            action.DynamicInvoke();
        }

        internal void TryAddAmplifier(FullBodyBipedIK fbbik, ChaControl chara)
        {
#if DEBUG
            LogDebug($"TryAddAmplifier:{fbbik}:{chara}");
#endif

            // To early for KKAPI.IsInsideHScene check.
#if KK
            if (fbbik == null || Manager.Scene.Instance == null || (!Manager.Scene.Instance.AddSceneName.Equals("HProc") && !ConfigRunOutsideH.Value)) return;
#elif KKS
            if (fbbik == null || (!Manager.Scene.AddSceneName.Equals("HProc") && !ConfigRunOutsideH.Value)) return;
#endif

            if (chara == null)
            {
                chara = fbbik.gameObject.GetComponentInParent<ChaControl>();
                if (chara == null) return;
            }

#if DEBUG
            LogDebug($"TryAddAmplifier:AfterCheck:{fbbik}:{chara}");
#endif

            var amplifier = fbbik.gameObject.GetComponent<Amplifier>();

            if (amplifier != null || _amplifierDic.ContainsKey(chara))
            {
                LogDebug($"Attempt to add second amplifier");
                return;
            }

            amplifier = fbbik.gameObject.AddComponent<Amplifier>();
            amplifier.ik = fbbik;
            amplifier.bodies = new Amplifier.Body[_amplifierDefinitionDic.Count];

            _amplifierDic.Add(chara, amplifier);

            for (int i = 0; i < amplifier.bodies.Length; i++)
            {
                var body = amplifier.bodies[i] = new Amplifier.Body();
                var def = _amplifierDefinitionDic.ElementAt(i).Value;

                body.relativeTo = fbbik.transform;
                body.transform = fbbik.transform.Find(def.transform);
                body.speed = def.speed;

                if (body.transform == null) continue;

                body.effectorLinks = new EffectorLink[def.effectorLinks.Length];
                for (var j = 0; j < body.effectorLinks.Length; j++)
                {
                    body.effectorLinks[j] = new(def.effectorLinks[j].effector, def.effectorLinks[j].weight);
                }
            }
#if VR
            // VR IK has late setup so it misses initial animator change.

            OnAnimatorChange(chara, amplifier);
#endif
        }

#if DEBUG

            // To record before/after video.
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.B))
            {
                foreach (var amplifier in _amplifierDic.Values)
                {
                    amplifier.enabled = !amplifier.enabled;
                }
            }    
        }
#endif
    }
}
