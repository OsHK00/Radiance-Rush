using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Modding;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.HookGen;
using UnityEngine;
using Preloads = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>>;

namespace RadianceRush
{
    public class ManagedMod
    {
        public string Name;
        public object Instance;
        public IMod Mod;
        public string Mode;

        public string DisplayName { get { return Mode == null ? Name : Name + " (" + Mode + ")"; } }
        public bool SameMod(ManagedMod other) { return other != null && other.Instance == Instance; }

        public Assembly Assembly { get { return Mod.GetType().Assembly; } }

        public string FileName
        {
            get
            {
                try { return System.IO.Path.GetFileNameWithoutExtension(Assembly.Location); }
                catch (Exception) { return null; }
            }
        }

        // Some mods (chongming, hag, sunset) can't be unloaded but check their own settings switch everywhere.
        public bool OwnSwitch
        {
            get
            {
                return SwitchOverrides.ContainsKey(Mod.GetType().FullName) || ModManager.Gated(Mod)
                    || (!(Mod is ITogglableMod) && UnloadMethod == null && SettingsField != null);
            }
        }

        static readonly string[] SwitchNames = { "Enabled", "on", "modOn" };

        static readonly Dictionary<string, string[]> SwitchOverrides = new Dictionary<string, string[]>
        {
            { "rogue.Rogue", new[] { "_set", "modboss_in_workshop" } }
        };

        const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public FieldInfo SwitchField(Type settings)
        {
            string[] over;
            if (SwitchOverrides.TryGetValue(Mod.GetType().FullName, out over)) return settings.GetField(over[1], AllFields);
            foreach (var n in SwitchNames)
            {
                var f = settings.GetField(n, BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(bool)) return f;
            }
            return null;
        }

        public FieldInfo SettingsField
        {
            get
            {
                string[] over;
                if (SwitchOverrides.TryGetValue(Mod.GetType().FullName, out over)) return Mod.GetType().GetField(over[0], AllFields);
                foreach (var f in Mod.GetType().GetFields(AllFields))
                    if (SwitchField(f.FieldType) != null) return f;
                return null;
            }
        }

        public object Settings
        {
            get
            {
                var f = SettingsField;
                return f == null ? null : f.GetValue(f.IsStatic ? null : Mod);
            }
        }

        bool OwnEnabled
        {
            get
            {
                if (ModManager.Gated(Mod)) return !ModManager.GatedOff(Mod);
                var s = Settings;
                var f = s == null ? null : SwitchField(s.GetType());
                return f != null && (bool)f.GetValue(s);
            }
        }

        // rngadiance have a public Unload() but dont declare ITogglableMod atleast is someting
        public MethodInfo UnloadMethod
        {
            get
            {
                return Mod is ITogglableMod ? null
                     : Mod.GetType().GetMethod("Unload", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }
        }
        public bool Enabled { get { return (bool)ModManager.FEnabled.GetValue(Instance) && (!OwnSwitch || OwnEnabled); } }
        public bool HasError { get { return ModManager.FError.GetValue(Instance) != null; } }
    }

    public static class ModManager
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        static readonly string[] LoadHooks = { "SavegameLoadHook", "AfterSavegameLoadHook", "NewGameHook" };

        static readonly Type Loader = typeof(Mod).Assembly.GetType("Modding.ModLoader");
        static readonly Type InstanceType = Loader.GetNestedType("ModInstance", Flags);
        static readonly PropertyInfo PInstances = Loader.GetProperty("ModInstances", Flags);
        static readonly MethodInfo MLoad = Loader.GetMethod("LoadMod", Flags);
        static readonly MethodInfo MUnload = Loader.GetMethod("UnloadMod", Flags);
        static readonly FieldInfo FMod = InstanceType.GetField("Mod", Flags);
        static readonly FieldInfo FName = InstanceType.GetField("Name", Flags);
        internal static readonly FieldInfo FEnabled = InstanceType.GetField("Enabled", Flags);
        internal static readonly FieldInfo FError = InstanceType.GetField("Error", Flags);

        public delegate void OrigLoadMod(object instance, bool updateModText, Preloads preloads);
        static readonly Dictionary<object, Preloads> preloadCache = new Dictionary<object, Preloads>();
        static readonly Dictionary<object, List<AssetBundle>> bundleCache = new Dictionary<object, List<AssetBundle>>();
        static Hook loadHook;

        public static void CapturePreloads()
        {
            if (loadHook == null)
                loadHook = new Hook(MLoad, typeof(ModManager).GetMethod("LoadModDetour", StaticFlags));
        }

        static void LoadModDetour(OrigLoadMod orig, object instance, bool updateModText, Preloads preloads)
        {
            if (instance == null) { orig(instance, updateModText, preloads); return; }
            if (preloads != null) preloadCache[instance] = preloads;
            else preloadCache.TryGetValue(instance, out preloads);

            // any rad 3.0 and quasar load a bundle with the same name, so whichever loads second (even at startup)
            // gets null. free the other one first, the stuff it already loaded stays
            var mod = FMod.GetValue(instance) as IMod;
            if (FightSetup.IsDreamStatueMod(mod))
                foreach (var other in new List<object>(bundleCache.Keys))
                    if (other != instance && FightSetup.IsDreamStatueMod(FMod.GetValue(other) as IMod)) FreeBundles(other);

            var before = new HashSet<AssetBundle>(AssetBundle.GetAllLoadedAssetBundles());
            orig(instance, updateModText, preloads);
            var loaded = new List<AssetBundle>();
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                if (!before.Contains(b)) loaded.Add(b);
            if (loaded.Count > 0) bundleCache[instance] = loaded;
        }

        static void FreeBundles(object instance)
        {
            List<AssetBundle> loaded;
            if (!bundleCache.TryGetValue(instance, out loaded)) return;
            foreach (var b in loaded)
                if (b != null) b.Unload(false);
            bundleCache.Remove(instance);
        }

        public static List<ManagedMod> GetMods()
        {
            var list = new List<ManagedMod>();
            foreach (object inst in (IEnumerable)PInstances.GetValue(null, null))
            {
                var mod = FMod.GetValue(inst) as IMod;
                if (mod != null) list.Add(new ManagedMod { Instance = inst, Mod = mod, Name = (string)FName.GetValue(inst) });
            }
            return list;
        }

        public static void Enable(ManagedMod m)
        {
            if (m.Enabled) return;
            if (m.OwnSwitch) { SetOwnSwitch(m, true); return; }
            FreeBundles(m.Instance);
            FightSetup.BeforeEnable(m);
            MLoad.Invoke(null, new object[] { m.Instance, true, null });
            if (m.Enabled) FireLoadHooks(m.Assembly);
        }

        public static void Disable(ManagedMod m)
        {
            if (!m.Enabled) return;
            if (m.OwnSwitch) { SetOwnSwitch(m, false); return; }
            try
            {
                if (m.Mod is ITogglableMod) MUnload.Invoke(null, new object[] { m.Instance, true });
                else
                {
                    // mods without Unload (like abskoth) only hook stuff, and Cleanup takes care of that
                    FEnabled.SetValue(m.Instance, false);
                    if (m.UnloadMethod != null) m.UnloadMethod.Invoke(m.Mod, null);
                }
            }
            finally
            {
                Cleanup(m.Assembly);
                FreeBundles(m.Instance);
            }
        }

        // The API unloads mods saved as disabled at startup without our cleanup, so their leftovers
        public static void CleanupIfDisabled(ManagedMod m)
        {
            if (!m.Enabled && !m.OwnSwitch) Cleanup(m.Assembly);
        }


        static readonly Dictionary<string, string> GatedMethods = new Dictionary<string, string>
        {
            { "ChallengeMode.ChallengeMode", "BeforeSceneLoadHook" }
        };
        static readonly HashSet<IMod> gatedOff = new HashSet<IMod>();
        static readonly Dictionary<Type, Hook> gateHooks = new Dictionary<Type, Hook>();

        public static bool Gated(IMod mod) { return GatedMethods.ContainsKey(mod.GetType().FullName); }
        public static bool GatedOff(IMod mod) { return gatedOff.Contains(mod); }

        static void SetGate(ManagedMod m, bool on)
        {
            var type = m.Mod.GetType();
            if (!gateHooks.ContainsKey(type))
            {
                var target = type.GetMethod(GatedMethods[type.FullName], Flags, null, new[] { typeof(string) }, null);
                var detour = typeof(ModManager).GetMethod("GatedSceneLoad", StaticFlags).MakeGenericMethod(type);
                gateHooks[type] = new Hook(target, detour);
            }
            if (on) gatedOff.Remove(m.Mod);
            else
            {
                gatedOff.Add(m.Mod);
                FightSetup.BeforeDisable(m);
            }
        }

        static string GatedSceneLoad<T>(Func<T, string, string> orig, T self, string scene)
        {
            return gatedOff.Contains(self as IMod) ? scene : orig(self, scene);
        }

        static void SetOwnSwitch(ManagedMod m, bool value)
        {
            if (Gated(m.Mod)) { SetGate(m, value); return; }
            var setter = m.Mod.GetType().GetMethod("SetEnabled", Flags, null, new[] { typeof(bool) }, null);
            if (setter != null) setter.Invoke(setter.IsStatic ? null : m.Mod, new object[] { value });
            else
            {
                var s = m.Settings;
                var f = s == null ? null : m.SwitchField(s.GetType());
                if (f != null) f.SetValue(s, value);
            }
        }

        public static void SetSetting(ManagedMod m, string name, object value)
        {
            var s = m.SettingsField == null ? null : m.Settings;
            var f = s == null ? null : s.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(s, value);
        }

        // the mod missed the real save load, so replay the first saveload hook it uses
        static void FireLoadHooks(Assembly asm)
        {
            var gm = GameManager.instance;
            if (gm == null) return;
            foreach (var name in LoadHooks)
            {
                var owned = Owned(typeof(ModHooks).GetField(name, StaticFlags), asm);
                if (owned.Count == 0) continue;
                object[] args = name == "SavegameLoadHook" ? new object[] { gm.profileID }
                              : name == "AfterSavegameLoadHook" ? new object[] { new SaveGameData(gm.playerData, gm.sceneData) }
                              : new object[0];
                foreach (var d in owned)
                {
                    try { d.DynamicInvoke(args); }
                    catch (Exception e) { RadianceRush.Instance.LogError(name + " of " + asm.GetName().Name + ": " + (e.InnerException ?? e)); }
                }
                return;
            }
        }

        static void Cleanup(Assembly asm)
        {
            if (asm == typeof(ModManager).Assembly) return;
            try { HookEndpointManager.RemoveAllOwnedBy(asm); }
            catch (Exception e) { RadianceRush.Instance.LogWarn("RemoveAllOwnedBy: " + e.Message); }

            Strip(typeof(ModHooks), asm);
            Strip(typeof(UnityEngine.SceneManagement.SceneManager), asm);

            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (mb != null && mb.GetType().Assembly == asm) UnityEngine.Object.Destroy(mb);
        }

        static void Strip(Type owner, Assembly asm)
        {
            foreach (var f in owner.GetFields(StaticFlags))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                var d = f.GetValue(null) as Delegate;
                if (d == null || Owned(f, asm).Count == 0) continue;
                var keep = new List<Delegate>(d.GetInvocationList());
                keep.RemoveAll(p => IsFrom(p, asm));
                f.SetValue(null, Delegate.Combine(keep.ToArray()));
            }
        }

        static List<Delegate> Owned(FieldInfo f, Assembly asm)
        {
            var result = new List<Delegate>();
            var d = f == null ? null : f.GetValue(null) as Delegate;
            if (d != null)
                foreach (var p in d.GetInvocationList())
                    if (IsFrom(p, asm)) result.Add(p);
            return result;
        }

        static bool IsFrom(Delegate d, Assembly asm)
        {
            var t = d.Method.DeclaringType;
            return t != null && t.Assembly == asm;
        }
    }
}
