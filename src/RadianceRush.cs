using System;
using System.Collections;
using System.Collections.Generic;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace RadianceRush
{
    public class RadianceRush : Mod, IGlobalSettings<Settings>, IMenuMod
    {
        const string Arena = "GG_Radiance";
        const string Workshop = "GG_Workshop";
        const string BenchRoom = "GG_Spa";
        const string ArenaGate = "door_dreamEnter";
        const float BenchExitX = 122.5f;

        public static RadianceRush Instance;
        public static Settings GS = new Settings();

        public readonly List<ManagedMod> Fights = new List<ManagedMod>();
        public int Index;
        public bool InRun;
        public bool Pending;

        FightList fightList = new FightList();
        // which fights have a bench right after them
        readonly HashSet<int> benchAfter = new HashSet<int>();
        bool won;
        bool resting;
        HeroState carried;
        Respawn respawnBeforeBench;
        BossSceneController.SetupEventDelegate savedSetup;
        Hud hud;

        public RadianceRush() : base("RadianceRush")
        {
            Instance = this;
            try { ModManager.CapturePreloads(); }
            catch (Exception e) { LogError("Could not hook LoadMod: " + e); }
        }

        public override string GetVersion() { return "0.15.1"; }
        public override int LoadPriority() { return 10000; }

        public override void Initialize()
        {
            ModHooks.BeforeSceneLoadHook += OnBeforeSceneLoad;
            ModHooks.AfterSavegameLoadHook += _ => ApplyStartState();
            ModHooks.NewGameHook += ApplyStartState;
            ModHooks.BeforeSavegameSaveHook += OnBeforeSave;
            On.GameManager.LoadGame += OnLoadGame;
            On.GameManager.BeginSceneTransition += OnBeginSceneTransition;
            On.BossSceneController.Awake += OnBossSceneAwake;
            On.BossSceneController.DoDreamReturn += OnDoDreamReturn;
            USceneManager.sceneLoaded += OnSceneLoaded;
            USceneManager.activeSceneChanged += OnActiveSceneChanged;

            var go = new GameObject("RadianceRushHud");
            UnityEngine.Object.DontDestroyOnLoad(go);
            hud = go.AddComponent<Hud>();
        }

        void RefreshFights()
        {
            var list = ReadFightList(true);
            if (list != null) fightList = list;
            Fights.Clear();
            Fights.AddRange(Resolve(fightList, true, benchAfter));
            if (Index >= Fights.Count) Index = 0;
        }

        public int LoadedModCount()
        {
            return Resolve(ReadFightList(false) ?? fightList, false, null).Count;
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

        List<ManagedMod> Resolve(FightList list, bool log, HashSet<int> benches)
        {
            var mods = ModManager.GetMods();
            var result = new List<ManagedMod>();
            if (benches != null) benches.Clear();
            foreach (var entry in list.Fights ?? new List<string>())
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (string.Equals(entry.Trim(), "Bench", StringComparison.OrdinalIgnoreCase))
                {
                    if (result.Count == 0) { if (log) LogWarn("Skipping 'Bench': it has to come after a fight"); }
                    else if (benches != null) benches.Add(result.Count - 1);
                    continue;
                }

                // "SUNSET:hard" = the mod's name plus which version you want
                int colon = entry.LastIndexOf(':');
                string name = (colon < 0 ? entry : entry.Substring(0, colon)).Trim();
                string mode = colon < 0 ? null : entry.Substring(colon + 1).Trim().ToLowerInvariant();
                var m = FindMod(mods, name);
                var modes = m == null ? new string[0] : FightSetup.ModesOf(m);
                string problem = m == null ? "not installed"
                               : m.Mod == this ? "is RadianceRush"
                               : m.HasError ? "failed to load"
                               : mode != null && Array.IndexOf(modes, mode) < 0
                                   ? "unknown mode '" + mode + "'" + (modes.Length > 0 ? " (use " + string.Join(", ", modes) + ")" : " (this mod has no modes)")
                               : null;
                if (problem != null) { if (log) LogWarn("Skipping '" + entry + "': " + problem); continue; }
                result.Add(new ManagedMod { Instance = m.Instance, Mod = m.Mod, Name = m.Name, Mode = mode });
            }
            return result;
        }

        // mod name first, then dll name (AnyRadiance (2) has the same mod name as 1.0, so that one needs the file name)
        static ManagedMod FindMod(List<ManagedMod> mods, string name)
        {
            return mods.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? mods.Find(x => string.Equals(x.FileName, name, StringComparison.OrdinalIgnoreCase));
        }

        // current fight's mod on, everything else in the list off
        void ApplyFight()
        {
            var target = Fights[Index];
            foreach (var m in Fights)
            {
                if (m.SameMod(target) || !m.Enabled) continue;
                try { ModManager.Disable(m); }
                catch (Exception e) { LogError("Failed to disable " + m.Name + ": " + e); }
            }
            if (!target.Enabled)
            {
                try
                {
                    ModManager.Enable(target);
                    if (!target.Enabled) LogError(target.Name + " could not be enabled (see API errors above).");
                }
                catch (Exception e) { LogError("Failed to enable " + target.Name + ": " + e); }
            }
            try { FightSetup.Apply(target, Fights); }
            catch (Exception e) { LogError("Fight setup for " + target.Name + " failed: " + (e.InnerException ?? e)); }
        }

        void ApplyStartState()
        {
            if (!GS.Enabled) return;
            ClearRun();
            RefreshFights();
            if (Fights.Count == 0) return;
            foreach (var m in Fights)
            {
                try { ModManager.CleanupIfDisabled(m); }
                catch (Exception e) { LogError("Cleanup of " + m.Name + " failed: " + (e.InnerException ?? e)); }
            }
            Index = 0;
            hud.StartCoroutine(ApplyWhenArenaUnloaded());
        }

        void ClearRun()
        {
            InRun = Pending = won = resting = false;
            carried = null;
            respawnBeforeBench = null;
        }

        // purity only adds its hooks on save load if its switch is on, so turn every own-switch fight on first
        void OnLoadGame(On.GameManager.orig_LoadGame orig, GameManager self, int slot, Action<bool> callback)
        {
            if (GS.Enabled)
            {
                RefreshFights();
                foreach (var m in Fights)
                {
                    if (!m.OwnSwitch) continue;
                    try { ModManager.Enable(m); }
                    catch (Exception e) { LogError("Failed to switch on " + m.Name + ": " + (e.InnerException ?? e)); }
                }
            }
            orig(self, slot, callback);
        }

        string OnBeforeSceneLoad(string scene)
        {
            if (!GS.Enabled)
            {
                ClearRun();
                return scene;
            }

            if (scene == Arena)
            {
                RefreshFights();
                if (Fights.Count == 0) { LogWarn("No fights configured."); return scene; }
                if (!InRun && !Pending) Index = 0;
                InRun = true;
                Pending = won = false;
                Log("Fight " + (Index + 1) + "/" + Fights.Count + ": " + Fights[Index].DisplayName);
                ApplyFight();
            }
            else if (InRun)
            {
                InRun = false;
                Pending = won && Index + 1 < Fights.Count;
                if (won && !Pending) Log("Rush complete!");
                else if (!won) Log("Lost fight " + (Index + 1) + ", back to the start.");
                carried = Pending ? HeroState.Capture() : null;
                if (!Pending) resting = false;
                Index = Pending ? Index + 1 : 0;
                won = false;
                hud.StartCoroutine(ApplyWhenArenaUnloaded());
            }
            return scene;
        }

        // Switch mods only once the old arena is gone, or its boss would pick up the next fightss changes
        IEnumerator ApplyWhenArenaUnloaded()
        {
            for (float t = 0f; t < 15f && (USceneManager.GetSceneByName(Arena).isLoaded
                                           || GameObject.Find("Absolute Radiance") != null); t += Time.unscaledDeltaTime)
                yield return null;
            yield return null;
            if (!InRun && Index < Fights.Count) ApplyFight();
        }

        void OnBossSceneAwake(On.BossSceneController.orig_Awake orig, BossSceneController self)
        {
            if (InRun && BossSceneController.SetupEvent != null) savedSetup = BossSceneController.SetupEvent;
            orig(self);
            if (InRun) self.OnBossesDead += OnBossesDead;
        }

        void OnBossesDead()
        {
            won = true;
            if (Index + 1 >= Fights.Count) hud.ShowGG();
        }

        // you won: bench room, straight into the next fight, or back to the workshop like normal
        void OnDoDreamReturn(On.BossSceneController.orig_DoDreamReturn orig, BossSceneController self)
        {
            bool next = GS.Enabled && InRun && won && Index + 1 < Fights.Count;
            bool needsWorkshop = next && FightSetup.NeedsWorkshopEntry(Fights[Index + 1]);
            if (needsWorkshop && (GS.AutoContinue || benchAfter.Contains(Index)))
                Log(Fights[Index + 1].DisplayName + " has to be entered from the workshop, going back there");

            if (next && !needsWorkshop && benchAfter.Contains(Index)) GoToBench();
            else if (next && !needsWorkshop && GS.AutoContinue) StartNextFight();
            else orig(self);
        }

        void StartNextFight()
        {
            won = false;
            Index++;
            Log("Fight " + (Index + 1) + "/" + Fights.Count + ": " + Fights[Index].DisplayName + " (auto continue)");
            HideOldRadiance();
            ApplyFight();
            if (savedSetup != null) BossSceneController.SetupEvent = savedSetup;
            carried = HeroState.Capture();
            Transition(Arena);
        }

        // the pantheon rest room. whatever door you take out of it, you end up in the next fight
        void GoToBench()
        {
            Log("Bench before fight " + (Index + 2) + "/" + Fights.Count);
            resting = true;
            respawnBeforeBench = Respawn.Capture();
            ResetBenchRoom();
            HideOldRadiance();
            Transition(BenchRoom);
        }

        static void ResetBenchRoom()
        {
            var sd = SceneData.instance;
            if (sd == null) return;
            if (sd.persistentBoolItems != null) sd.persistentBoolItems.RemoveAll(x => x.sceneName == BenchRoom);
            if (sd.persistentIntItems != null) sd.persistentIntItems.RemoveAll(x => x.sceneName == BenchRoom);
        }

        // the hud slides in and out through its "Slide Out" fsm, OUT hides it and IN brings it back
        static void ShowHud()
        {
            PlayMakerFSM.BroadcastEvent("HUD IN");
            var cams = GameCameras.instance;
            var slide = cams == null || cams.hudCanvas == null ? null : FSMUtility.LocateMyFSM(cams.hudCanvas, "Slide Out");
            if (slide != null) slide.SendEvent("IN");
        }

        IEnumerator ShowHudLater()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (resting) ShowHud();
        }

        IEnumerator WatchBenchExit()
        {
            yield return WaitForHero();
            // the last boss's death hid the hud, and the dream return that brings it back got skipped
            ShowHud();
            FightSetup.RemoveExtraBenchObjects();
            FightSetup.RemoveDreamchecks(Fights);
            // the room sometimes hides it again while it's still loading in, so do it once more
            hud.StartCoroutine(ShowHudLater());
            while (resting && USceneManager.GetActiveScene().name == BenchRoom)
            {
                var hc = HeroController.instance;
                if (hc != null && !GameManager.instance.IsInSceneTransition && !hc.cState.transitioning
                    && hc.transform.position.x >= BenchExitX)
                {
                    // OnBeginSceneTransition takes it from here
                    GameManager.instance.BeginSceneTransition(LoadInfo(Arena));
                    yield break;
                }
                yield return null;
            }
        }

        void OnBeginSceneTransition(On.GameManager.orig_BeginSceneTransition orig, GameManager self, GameManager.SceneLoadInfo info)
        {
            if (resting && GS.Enabled && info != null && info.SceneName != BenchRoom
                && USceneManager.GetActiveScene().name == BenchRoom
                && !info.SceneName.StartsWith("Menu") && info.SceneName != "Quit_To_Menu")
            {
                resting = false;
                Log("Leaving the bench to fight " + (Index + 1) + "/" + Fights.Count);
                if (respawnBeforeBench != null) respawnBeforeBench.Restore(PlayerData.instance);
                respawnBeforeBench = null;
                // take what the knight has after the bench, not what it had after the last fight
                carried = HeroState.Capture();
                if (savedSetup != null) BossSceneController.SetupEvent = savedSetup;
                info.SceneName = Arena;
                info.EntryGateName = ArenaGate;
                info.Visualization = GameManager.SceneLoadVisualizations.GodsAndGlory;
                hud.StartCoroutine(EnsureHeroVisible());
                hud.StartCoroutine(FadeInAfter(Arena));
            }
            orig(self, info);
        }

        // sitting on that bench makes it your respawn and saves the game, so put the old respawn back in the save
        void OnBeforeSave(SaveGameData data)
        {
            if (!resting || respawnBeforeBench == null) return;
            respawnBeforeBench.Restore(data.playerData);
            respawnBeforeBench.Restore(PlayerData.instance);
        }

        static GameManager.SceneLoadInfo LoadInfo(string scene)
        {
            return new GameManager.SceneLoadInfo
            {
                SceneName = scene,
                EntryGateName = ArenaGate,
                Visualization = GameManager.SceneLoadVisualizations.GodsAndGlory,
                AlwaysUnloadUnusedAssets = true
            };
        }

        void Transition(string scene)
        {
            hud.StartCoroutine(EnsureHeroVisible());
            hud.StartCoroutine(FadeInAfter(scene));
            GameManager.instance.BeginSceneTransition(LoadInfo(scene));
        }

        static void HideOldRadiance()
        {
            for (int i = 0; i < 8; i++)
            {
                var old = GameObject.Find("Absolute Radiance");
                if (old == null) break;
                old.SetActive(false);
            }
        }

        IEnumerator FadeInAfter(string scene)
        {
            
            for (float t = 0f; t < 2f && !GameManager.instance.IsInSceneTransition; t += Time.unscaledDeltaTime)
                yield return null;
            for (float t = 0f; t < 15f && (USceneManager.GetActiveScene().name != scene
                                           || GameManager.instance.IsInSceneTransition); t += Time.unscaledDeltaTime)
                yield return null;
            if (USceneManager.GetActiveScene().name == scene) GameManager.instance.FadeSceneIn();
        }

        static IEnumerator WaitForHero()
        {
            yield return null;
            for (float t = 0f; t < 15f && (GameManager.instance.IsInSceneTransition || HeroController.instance == null
                                           || HeroController.instance.cState.transitioning); t += Time.unscaledDeltaTime)
                yield return null;
        }

        IEnumerator EnsureHeroVisible()
        {
            yield return WaitForHero();
            var hc = HeroController.instance;
            if (hc == null) yield break;
            var renderer = hc.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = true;
            var sprite = hc.GetComponent<tk2dSprite>();
            if (sprite != null && sprite.color.a < 1f)
            {
                var c = sprite.color;
                c.a = 1f;
                sprite.color = c;
            }
        }

        IEnumerator ApplyCarried(HeroState state)
        {
            yield return WaitForHero();
            // the arena messes with hp right after you land, so wait a bit
            yield return new WaitForSecondsRealtime(0.5f);
            var hc = HeroController.instance;
            if (hc == null) yield break;
            try
            {
                if (GS.CarryOver) state.Restore(hc);
                else state.Reset(hc);
            }
            catch (Exception e) { LogError("Could not apply HP/soul between fights: " + e); }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!GS.Enabled) return;
            bool hid = false;
            for (int i = 0; i < USceneManager.sceneCount; i++)
            {
                var old = USceneManager.GetSceneAt(i);
                if (old.name != Arena || old.handle == scene.handle || !old.isLoaded) continue;
                foreach (var root in old.GetRootGameObjects()) root.SetActive(false);
                hid = true;
            }
            if (!hid) return;
            ResetCameraZoom();
        }

        // Some mods (Aura (mb), ImmortalLight, RNGRadiance) change the zoom and dont restore it
        void ResetCameraZoom()
        {
            var cams = GameCameras.instance;
            if (cams != null && cams.tk2dCam != null) cams.tk2dCam.ZoomFactor = 1f;
        }

        void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (!GS.Enabled) return;
            if (resting && to.name == BenchRoom) hud.StartCoroutine(WatchBenchExit());
            if (!InRun || to.name != Arena || Index >= Fights.Count) return;
            hud.StartCoroutine(AfterArenaLoaded(Fights[Index], from.name == Workshop));
            if (FightSetup.PuritySetsVoidDamage(Fights[Index], Fights)) hud.StartCoroutine(KeepVoidDamage(to.handle));
            if (carried != null) hud.StartCoroutine(ApplyCarried(carried));
            carried = null;
        }

        // purity bumps the void damage again a bit after the arena loads, so keep fixing it the whole fight
        IEnumerator KeepVoidDamage(int arena)
        {
            yield return null;
            var scene = USceneManager.GetActiveScene();
            if (scene.handle != arena) yield break;
            var voids = FightSetup.FindVoids(scene);
            while (GS.Enabled && USceneManager.GetActiveScene().handle == arena)
            {
                FightSetup.ResetVoidDamage(voids);
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        IEnumerator AfterArenaLoaded(ManagedMod fight, bool fromWorkshop)
        {
            yield return null;
            try { FightSetup.AfterArenaLoaded(fight, Fights, fromWorkshop); }
            catch (Exception e) { LogError("Arena setup for " + fight.Name + " failed: " + (e.InnerException ?? e)); }
        }

        public void OnLoadGlobal(Settings s) { GS = s ?? new Settings(); }
        public Settings OnSaveGlobal() { return GS; }

        public bool ToggleButtonInsideMenu { get { return false; } }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) { return ModMenu.Build(GS, LoadedModCount); }
    }
}
