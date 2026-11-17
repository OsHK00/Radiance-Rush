using System;
using System.Collections;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace RadianceRush
{
    public class RadianceRush : Mod, IGlobalSettings<Settings>, IMenuMod
    {
        const string Arena = "GG_Radiance";

        public static RadianceRush Instance;
        public static Settings GS = new Settings();

        public readonly List<ManagedMod> Fights = new List<ManagedMod>();
        public int Index;
        public bool InRun;
        public bool Pending;
        bool won;
        Hud hud;
        FightList fightList = new FightList();

        public RadianceRush() : base("RadianceRush")
        {
            Instance = this;
            try { ModManager.CapturePreloads(); }
            catch (Exception e) { LogError("Could not hook LoadMod: " + e); }
        }

        public override string GetVersion() { return "0.3.0"; }
        public override int LoadPriority() { return 10000; }

        public override void Initialize()
        {
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            On.BossSceneController.Awake += OnBossSceneAwake;
            var go = new GameObject("RadianceRushHud");
            UnityEngine.Object.DontDestroyOnLoad(go);
            hud = go.AddComponent<Hud>();
        }

        void RefreshFights()
        {
            var list = ReadFightList(true);
            if (list != null) fightList = list;
            Fights.Clear();
            Fights.AddRange(Resolve(fightList, true));
            if (Index >= Fights.Count) Index = 0;
        }

        public int LoadedModCount()
        {
            return Resolve(ReadFightList(false) ?? fightList, false).Count;
        }

        FightList ReadFightList(bool log)
        {
            if (!System.IO.File.Exists(FightList.FilePath))
            {
                if (log) LogError("Missing " + FightList.FilePath + ". Copy fights.json next to RadianceRush.dll.");
                return null;
            }
            try { return FightList.Load(); }
            catch (Exception e)
            {
                if (log) LogError("Could not read " + FightList.FilePath + ", keeping the previous list: " + e.Message);
                return null;
            }
        }

        List<ManagedMod> Resolve(FightList list, bool log)
        {
            var mods = ModManager.GetMods();
            var result = new List<ManagedMod>();
            foreach (var name in list.Fights ?? new List<string>())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var m = mods.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                string problem = m == null ? "not installed"
                               : m.Mod == this ? "is RadianceRush"
                               : !m.Togglable ? "cannot be toggled (no ITogglableMod)"
                               : m.HasError ? "failed to load"
                               : null;
                if (problem != null) { if (log) LogWarn("Skipping '" + name + "': " + problem); }
                else if (!result.Exists(x => x.Instance == m.Instance)) result.Add(m);
            }
            return result;
        }

        int ResetIndex()
        {
            return fightList.StartIndex >= 0 && fightList.StartIndex < Fights.Count ? fightList.StartIndex : 0;
        }

        void ApplyFight()
        {
            var target = Fights[Index];
            foreach (var m in Fights)
            {
                if (m == target || !m.Enabled) continue;
                try { ModManager.Disable(m); Log("Disabled " + m.Name); }
                catch (Exception e) { LogError("Failed to disable " + m.Name + ": " + e); }
            }
            if (target.Enabled) return;
            try
            {
                string fired = ModManager.Enable(target);
                if (target.Enabled) Log("Enabled " + target.Name + (fired != null ? " (replayed " + fired + ")" : ""));
                else LogError(target.Name + " could not be enabled (see API errors above).");
            }
            catch (Exception e) { LogError("Failed to enable " + target.Name + ": " + e); }
        }

        string OnBeforeSceneLoad(string scene)
        {
            if (!GS.Enabled)
            {
                InRun = Pending = won = false;
                return scene;
            }

            if (scene == Arena)
            {
                RefreshFights();
                if (Fights.Count == 0) { LogWarn("No fights configured."); return scene; }
                if (!InRun && !Pending) Index = ResetIndex();
                InRun = true;
                Pending = won = false;
                Log("Fight " + (Index + 1) + "/" + Fights.Count + ": " + Fights[Index].Name);
                ApplyFight();
            }
            else if (InRun)
            {
                InRun = false;
                Pending = won && Index + 1 < Fights.Count;
                if (won && !Pending) Log("Rush complete!");
                else if (!won) Log("Lost fight " + (Index + 1) + ", back to the start.");
                Index = Pending ? Index + 1 : ResetIndex();
                won = false;
                hud.StartCoroutine(ApplyWhenArenaUnloaded());
            }
            return scene;
        }

        // switch only after the old arena is gone... holy
        IEnumerator ApplyWhenArenaUnloaded()
        {
            for (float t = 0f; t < 15f && (UnityEngine.SceneManagement.SceneManager.GetSceneByName(Arena).isLoaded
                                           || GameObject.Find("Absolute Radiance") != null); t += Time.unscaledDeltaTime)
                yield return null;
            yield return null;
            if (!InRun && Index < Fights.Count) ApplyFight();
        }

        void OnBossSceneAwake(On.BossSceneController.orig_Awake orig, BossSceneController self)
        {
            orig(self);
            if (InRun) self.OnBossesDead += OnBossesDead;
        }

        void OnBossesDead()
        {
            won = true;
            if (Index + 1 >= Fights.Count) hud.ShowGG();
        }

        public void OnLoadGlobal(Settings s) { GS = s ?? new Settings(); }
        public Settings OnSaveGlobal() { return GS; }

        public bool ToggleButtonInsideMenu { get { return false; } }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) { return ModMenu.Build(GS, LoadedModCount); }
    }
}
