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

        public Assembly Assembly { get { return Mod.GetType().Assembly; } }
        public bool Togglable { get { return Mod is ITogglableMod; } }
        public bool Enabled { get { return (bool)ModManager.FEnabled.GetValue(Instance); } }
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
        static Hook loadHook;

        public static void CapturePreloads()
        {
            if (loadHook == null)
                loadHook = new Hook(MLoad, typeof(ModManager).GetMethod("LoadModDetour", StaticFlags));
        }

        static void LoadModDetour(OrigLoadMod orig, object instance, bool updateModText, Preloads preloads)
        {
            if (instance != null)
            {
                if (preloads != null) preloadCache[instance] = preloads;
                else preloadCache.TryGetValue(instance, out preloads);
            }
            orig(instance, updateModText, preloads);
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

        public static string Enable(ManagedMod m)
        {
            if (m.Enabled) return null;
            MLoad.Invoke(null, new object[] { m.Instance, true, null });
            return m.Enabled ? FireLoadHooks(m.Assembly) : null;
        }

        public static void Disable(ManagedMod m)
        {
            if (!m.Enabled) return;
            MUnload.Invoke(null, new object[] { m.Instance, true });
            Cleanup(m.Assembly);
        }

        static string FireLoadHooks(Assembly asm)
        {
            var gm = GameManager.instance;
            if (gm == null) return null;
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
                return name;
            }
            return null;
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
