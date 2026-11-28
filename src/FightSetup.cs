using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Modding;
using UnityEngine;

namespace RadianceRush
{
    // Permod tweaks applied around each fight so they can work 
    public static class FightSetup
    {
        const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // versions you can pick with "Name:mode", the first one is what you get if you don pick
        public static string[] ModesOf(ManagedMod m)
        {
            switch (m.Mod.GetType().FullName)
            {
                case "OrbRadiance.OrbRadiance": return new[] { "easy", "normal", "hard" };
                case "RadianceBoss.RadianceBoss": return new[] { "normal", "hard" };
                case "AllHallownestEnhanced.AllHallownestEnhanced": return new[] { "hard", "normal" };
                // how many markoths
                case "Abskoth.Abskoth": return new[] { "1", "2", "3", "4" };
            }
            // sunset and purity: hard = their dream version
            return SettingSunType(m) != null ? new[] { "normal", "hard" } : new string[0];
        }

        static string ModeOf(ManagedMod m)
        {
            var modes = ModesOf(m);
            return m.Mode ?? (modes.Length > 0 ? modes[0] : null);
        }

        // runs right before the arena loads
        public static void Apply(ManagedMod fight, List<ManagedMod> fights)
        {
            // any rad 3.0 and quasar only kick in if the statue is on its dream version
            if (fights.Exists(IsDreamStatueMod)) SetDreamVersion(IsDreamStatueMod(fight));

            string mode = ModeOf(fight);
            switch (fight.Mod.GetType().FullName)
            {
                case "OrbRadiance.OrbRadiance": SetChongmingMode(fight, mode); break;
                case "RadianceBoss.RadianceBoss": ModManager.SetSetting(fight, "HardMode", mode == "hard"); break;
                case "UltimatumRadiance.AtomicRadiance": AddAtomicFinder(fight); break;
                // enhanced_2Radiant is the "!!二次加强无上福光!!" option in its menu, the extra hard radiance
                case "AllHallownestEnhanced.AllHallownestEnhanced":
                    ModManager.SetSetting(fight, "EnhanceBOSS", true);
                    ModManager.SetSetting(fight, "enhanced_2Radiant", mode == "hard");
                    break;
                case "Abskoth.Abskoth": SetMarkoths(fight, int.Parse(mode)); break;
            }
            var sun = SettingSunType(fight);
            if (sun != null)
            {
                SetStatic(fight, sun, "DREAMSWITCH", mode == "hard");
                SetStatic(fight, sun, "HardMode", mode == "hard" ? 1 : 0);
            }
        }

        // sunset sets stuff up in the workshop and freezes the knight if you come straight from another arena
        //also radaince skin breaks teh fight 
        public static bool NeedsWorkshopEntry(ManagedMod m)
        {
            return m.Mod.GetType().FullName == "SETTINGSUN_RADIANCE.SUNSET_RADIANCE+SUNSET";
        }

        static bool IsDreamStatueMod(ManagedMod m) { return IsDreamStatueMod(m.Mod); }

        // any rad 3.0 and quasar are pretty much the same code. quasar's renamed copy keeps its finder under
        // ShatteredQuasar.RadianceFinder, so go by the class name only
        public static bool IsDreamStatueMod(IMod mod)
        {
            if (mod == null) return false;
            try
            {
                foreach (var t in mod.GetType().Assembly.GetTypes())
                    if (t.Name == "RadianceFinder" && t.GetMethod("ChangeStatue", InstanceFlags) != null) return true;
            }
            catch (ReflectionTypeLoadException) { }
            return false;
        }

        static void SetDreamVersion(bool value)
        {
            var pd = PlayerData.instance;
            if (pd == null) return;
            var state = pd.statueStateRadiance;
            state.usingAltVersion = value;
            pd.statueStateRadiance = state;
        }

        public static void RemoveDreamchecks(List<ManagedMod> fights)
        {
            var cams = GameCameras.instance;
            if (cams == null || cams.tk2dCam == null) return;
            foreach (var m in fights)
            {
                var sun = SettingSunType(m);
                var t = sun == null ? null : m.Assembly.GetType(sun + "+Dreamcheck2");
                var c = t == null ? null : cams.tk2dCam.GetComponent(t);
                if (c != null) UnityEngine.Object.Destroy(c);
            }
        }

        // if AllHallownestEnhanced is on when you walk into the bench room (so the bench right before its fight)
        public static void RemoveExtraBenchObjects()
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == "RestBench (1)(Clone)" || go.name == "Soul Totem white_Infinte(Clone)")
                    UnityEngine.Object.Destroy(go);
        }

        // before turning a mod back on. any rad 3.0 and quasar Add() their stuff to dictionaries on every
        // Initialize and never clear them, so the second time itd blow up so
        public static void BeforeEnable(ManagedMod m)
        {
            if (!IsDreamStatueMod(m)) return;
            foreach (var f in m.Mod.GetType().GetFields(InstanceFlags))
            {
                var dict = f.GetValue(m.Mod) as IDictionary;
                if (dict != null) dict.Clear();
            }
        }

        static string SettingSunType(ManagedMod m)
        {
            switch (m.Mod.GetType().FullName)
            {
                case "SETTINGSUN_RADIANCE.SUNSET_RADIANCE+SUNSET": return "SETTINGSUN_RADIANCE.SUNSET_RADIANCE";
                case "PURITY.PURITY": return "PURITY.PURITY";
                default: return null;
            }
        }

        // a frame after the arena is up
        public static void AfterArenaLoaded(ManagedMod fight, List<ManagedMod> fights, bool fromWorkshop)
        {
            if (fight.Mod.GetType().FullName == "DoubleRadiance.DoubleRadiance" && !fromWorkshop) StartDoubleRadiance(fight);

            if (fight.Mod.GetType().FullName == "OrbRadiance.OrbRadiance") SetChongmingMode(fight, ModeOf(fight));
            var sun = SettingSunType(fight);
            if (sun != null) SetStatic(fight, sun, "HardMode", ModeOf(fight) == "hard" ? 1 : 0);

            RemoveDreamchecks(fights);
            var cams = GameCameras.instance;
            if (cams == null || cams.tk2dCam == null) return;

            // their zoom thing (Away) only does anything for 4s after it's created. two of their fights back to
            // back reuse the same one, already past that, so the second fight was stuck with normal zoom
            var away = sun == null ? null : fight.Assembly.GetType(sun + "+Away");
            var zoom = away == null ? null : cams.tk2dCam.GetComponent(away) as Behaviour;
            if (zoom != null)
            {
                away.GetField("Timer", InstanceFlags).SetValue(zoom, 0f);
                away.GetField("Stop", InstanceFlags).SetValue(zoom, false);
                zoom.enabled = true;
            }

            foreach (var m in fights)
            {
                var other = SettingSunType(m);
                if (other == null || m.SameMod(fight)) continue;
                foreach (var name in new[] { "+Away", "+ChangeColor" })
                {
                    var t = m.Assembly.GetType(other + name);
                    var c = t == null ? null : cams.tk2dCam.GetComponent(t);
                    if (c != null) UnityEngine.Object.Destroy(c);
                }
            }
        }

        public static bool PuritySetsVoidDamage(ManagedMod fight, List<ManagedMod> fights)
        {
            return fight.Mod.GetType().FullName != "PURITY.PURITY" && fights.Exists(x => x.Mod.GetType().FullName == "PURITY.PURITY");
        }

        public class Voids
        {
            public readonly List<DamageHero> Pits = new List<DamageHero>();
            public readonly List<HutongGames.PlayMaker.FsmInt> Clouds = new List<HutongGames.PlayMaker.FsmInt>();
        }

        public static Voids FindVoids(UnityEngine.SceneManagement.Scene arena)
        {
            var v = new Voids();
            foreach (var root in arena.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Spike Collider" && t.parent != null && t.parent.name == "Abyss Pit")
                    {
                        var damage = t.GetComponent<DamageHero>();
                        if (damage != null) v.Pits.Add(damage);
                    }
                    else if (t.name == "Cloud Hazard")
                    {
                        var fsm = FSMUtility.LocateMyFSM(t.gameObject, "damages_hero");
                        var dealt = fsm == null ? null : fsm.FsmVariables.FindFsmInt("damageDealt");
                        if (dealt != null) v.Clouds.Add(dealt);
                    }
                }
            }
            return v;
        }

        public static void ResetVoidDamage(Voids v)
        {
            foreach (var pit in v.Pits)
                if (pit != null) pit.damageDealt = 1;
            foreach (var cloud in v.Clouds)
                cloud.Value = 1;
        }

        static void SetStatic(ManagedMod m, string type, string field, object value)
        {
            var t = m.Assembly.GetType(type);
            var f = t == null ? null : t.GetField(field, StaticFlags);
            if (f != null) f.SetValue(null, value);
        }

        // chgming picks its mode from its statue page and wipes it after every fight
        static void SetChongmingMode(ManagedMod m, string mode)
        {
            var state = m.Assembly.GetType("OrbRadiance.BattleModeState");
            var modeType = m.Assembly.GetType("OrbRadiance.BattleMode");
            var set = state == null ? null : state.GetMethod("Set", StaticFlags);
            if (set == null || modeType == null) return;
            set.Invoke(null, new[] { Enum.Parse(modeType, mode, true) });
        }

        public static void BeforeDisable(ManagedMod m)
        {
            if (m.Mod.GetType().FullName != "ChallengeMode.ChallengeMode") return;
            var control = m.Mod.GetType().GetField("modifierControl", StaticFlags);
            var mc = control == null ? null : control.GetValue(null) as MonoBehaviour;
            var unload = mc == null ? null : mc.GetType().GetMethod("Unload", InstanceFlags);
            if (unload != null) unload.Invoke(mc, null);
        }

        // abskoth keeps its menu stuff in a static GlobalSaveData, no on/off switch in there
        static void SetMarkoths(ManagedMod m, int count)
        {
            var prop = m.Mod.GetType().GetProperty("GlobalSaveData", StaticFlags);
            var data = prop == null ? null : prop.GetValue(null, null);
            var field = data == null ? null : data.GetType().GetField("numMarkoths");
            if (field != null) field.SetValue(data, count);
        }

        // atomic only adds its finder when the knight spawns on save load, but i turn it on mid run
        static void AddAtomicFinder(ManagedMod m)
        {
            var finderType = m.Assembly.GetType("UltimatumRadiance.AbsFinder");
            if (finderType == null || GameManager.instance == null) return;
            if (GameManager.instance.GetComponent(finderType) == null) GameManager.instance.gameObject.AddComponent(finderType);
        }

        // doubleradiance only looks for the boss when you come in from the workshop, and auto continue skips that
        static void StartDoubleRadiance(ManagedMod m)
        {
            var finderType = m.Assembly.GetType("DoubleRadiance.AbsFinder");
            var find = finderType == null ? null : finderType.GetMethod("FindAbs", InstanceFlags);
            if (find == null || GameManager.instance == null) return;
            var finder = GameManager.instance.GetComponent(finderType) as MonoBehaviour;
            if (finder == null) return;
            finder.StartCoroutine((IEnumerator)find.Invoke(finder, null));
        }
    }
}
