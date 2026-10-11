using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastVaccine
{
    public enum GamePhase { Initializing, Ready, WaveActive, Intermission, FinalSearch, Victory, GameOver, Paused }
    public enum ZombieKind { Bunny, Bear }

    [Serializable]
    public class VaccineZone
    {
        public string id;
        public string displayName;
        public string landmark;
        public Transform vaccinePoint;
        public Light zoneLight;
    }

    public sealed class LastVaccineGame : MonoBehaviour
    {
        public static LastVaccineGame Instance { get; private set; }

        [Header("Timeline")]
        [SerializeField] float missionDuration = 540f;
        [SerializeField] float waveDuration = 50f;
        [SerializeField] float intermissionDuration = 20f;
        [SerializeField] float finalSearchDuration = 190f;
        [SerializeField] float poweredLightDuration = 30f;

        [Header("Wave Balance")]
        [SerializeField] int[] maxAliveByWave = { 5, 7, 9, 11, 13 };
        [SerializeField] float[] spawnIntervalByWave = { 4.8f, 4.2f, 3.6f, 3.0f, 2.4f };
        [SerializeField] int finalSearchMaxAlive = 8;
        [SerializeField] float finalSearchSpawnInterval = 5.5f;

        [Header("Scene References")]
        public FpsSurvivor player;
        public ZombieAgent bunnyTemplate;
        public ZombieAgent bearTemplate;
        public Transform[] enemySpawnPoints;
        public VaccinePickup vaccine;
        public VaccineZone[] zones;

        public GamePhase Phase { get; private set; } = GamePhase.Initializing;
        public int Wave { get; private set; }
        public float PhaseTimeRemaining { get; private set; }
        public float MissionTimeRemaining { get; private set; }
        public float ActiveLightRemaining { get; private set; }
        public string FailureReason { get; private set; } = string.Empty;
        public IReadOnlyList<string> Clues => clues;

        readonly List<ZombieAgent> alive = new List<ZombieAgent>(32);
        readonly List<string> clues = new List<string>(5);
        int targetZone;
        int selectedLight = -1;
        bool rewardChosen;
        bool journalOpen;
        float spawnCountdown;
        GamePhase phaseBeforePause;
        GUIStyle titleStyle;
        GUIStyle hudStyle;
        GUIStyle centerStyle;
        GUIStyle clueStyle;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            Time.timeScale = 1f;
            ResolveRuntimeReferences();
            ConfigureZoneLights();
            MissionTimeRemaining = missionDuration;
            targetZone = zones != null && zones.Length > 0 ? UnityEngine.Random.Range(0, zones.Length) : 0;
            if (vaccine != null && zones != null && zones.Length > 0)
            {
                vaccine.transform.SetPositionAndRotation(zones[targetZone].vaccinePoint.position, Quaternion.identity);
                vaccine.gameObject.SetActive(false);
            }
            SetAllZoneLights(false);
            LockCursor(true);
            BeginWave(1);
        }

        void ConfigureZoneLights()
        {
            if (zones == null) return;
            foreach (VaccineZone zone in zones)
            {
                if (zone == null || zone.zoneLight == null) continue;
                zone.zoneLight.range = 17f;
                zone.zoneLight.intensity = 8f;
                zone.zoneLight.color = new Color(.35f, .72f, 1f);
            }
        }

        void ResolveRuntimeReferences()
        {
            if (player == null) player = FindFirstObjectByType<FpsSurvivor>(FindObjectsInactive.Include);
            if (vaccine == null) vaccine = FindFirstObjectByType<VaccinePickup>(FindObjectsInactive.Include);
            if (bunnyTemplate == null || bearTemplate == null)
            {
                ZombieAgent[] templates = FindObjectsByType<ZombieAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (ZombieAgent candidate in templates)
                {
                    if (candidate.kind == ZombieKind.Bunny && bunnyTemplate == null) bunnyTemplate = candidate;
                    if (candidate.kind == ZombieKind.Bear && bearTemplate == null) bearTemplate = candidate;
                }
            }
            if (player == null || vaccine == null || bunnyTemplate == null || bearTemplate == null)
                Debug.LogError("[Last Vaccine] Required gameplay reference could not be resolved.");
        }

        void Update()
        {
            bool escape = KeyDownEscape();
            if (escape && Phase != GamePhase.Victory && Phase != GamePhase.GameOver)
            {
                if (Phase == GamePhase.Paused) Resume(); else Pause();
            }
            if (KeyDownJournal() && Phase != GamePhase.Paused) journalOpen = !journalOpen;
            if (Phase == GamePhase.Paused || Phase == GamePhase.Victory || Phase == GamePhase.GameOver) return;

            MissionTimeRemaining = Mathf.Max(0f, MissionTimeRemaining - Time.deltaTime);
            if (MissionTimeRemaining <= 0f) { GameOver("MISSION TIME EXPIRED"); return; }

            if (ActiveLightRemaining > 0f)
            {
                ActiveLightRemaining -= Time.deltaTime;
                if (ActiveLightRemaining <= 0f) SetAllZoneLights(false);
                else if (selectedLight >= 0)
                    zones[selectedLight].zoneLight.enabled = ActiveLightRemaining > 5f || Mathf.FloorToInt(ActiveLightRemaining * 6f) % 2 != 0;
            }

            PhaseTimeRemaining = Mathf.Max(0f, PhaseTimeRemaining - Time.deltaTime);
            if (Phase == GamePhase.WaveActive)
            {
                UpdateSpawning(false);
                if (PhaseTimeRemaining <= 0f) BeginIntermission();
            }
            else if (Phase == GamePhase.Intermission)
            {
                if (PhaseTimeRemaining <= 0f)
                {
                    if (!rewardChosen) AutoReward();
                    if (Wave >= 5) BeginFinalSearch(); else BeginWave(Wave + 1);
                }
            }
            else if (Phase == GamePhase.FinalSearch)
            {
                UpdateSpawning(true);
                if (PhaseTimeRemaining <= 0f) GameOver("VACCINE SIGNAL LOST");
            }
        }

        void BeginWave(int index)
        {
            Wave = index;
            Phase = GamePhase.WaveActive;
            PhaseTimeRemaining = waveDuration;
            spawnCountdown = 1.5f;
            rewardChosen = false;
            journalOpen = false;
            ActivateSelectedLight();
        }

        void BeginIntermission()
        {
            Phase = GamePhase.Intermission;
            PhaseTimeRemaining = intermissionDuration;
            rewardChosen = false;
            selectedLight = -1;
            SetAllZoneLights(false);
            DespawnAllEnemies();
            AddClueForWave(Wave);
            LockCursor(false);
        }

        void BeginFinalSearch()
        {
            Phase = GamePhase.FinalSearch;
            PhaseTimeRemaining = Mathf.Min(finalSearchDuration, MissionTimeRemaining);
            spawnCountdown = 4f;
            if (vaccine != null) vaccine.gameObject.SetActive(true);
            ActivateSelectedLight();
            LockCursor(true);
        }

        void UpdateSpawning(bool finalSearch)
        {
            for (int i = alive.Count - 1; i >= 0; i--) if (alive[i] == null) alive.RemoveAt(i);
            int maxAlive = finalSearch ? finalSearchMaxAlive : 3 + Wave * 2;
            if (!finalSearch && maxAliveByWave != null && maxAliveByWave.Length > 0)
                maxAlive = maxAliveByWave[Mathf.Clamp(Wave - 1, 0, maxAliveByWave.Length - 1)];
            if (alive.Count >= maxAlive) return;
            spawnCountdown -= Time.deltaTime;
            if (spawnCountdown > 0f) return;
            spawnCountdown = finalSearch ? finalSearchSpawnInterval : Mathf.Max(2f, 5.5f - Wave * .65f);
            if (!finalSearch && spawnIntervalByWave != null && spawnIntervalByWave.Length > 0)
                spawnCountdown = spawnIntervalByWave[Mathf.Clamp(Wave - 1, 0, spawnIntervalByWave.Length - 1)];
            SpawnEnemy(finalSearch || (Wave >= 2 && UnityEngine.Random.value < 0.18f + Wave * 0.08f));
        }

        void SpawnEnemy(bool bearChance)
        {
            if (player == null || enemySpawnPoints == null || enemySpawnPoints.Length == 0) return;
            Transform chosen = null;
            float best = -1f;
            for (int attempt = 0; attempt < enemySpawnPoints.Length * 2; attempt++)
            {
                Transform point = enemySpawnPoints[UnityEngine.Random.Range(0, enemySpawnPoints.Length)];
                float distance = Vector3.Distance(point.position, player.transform.position);
                if (distance >= 12f) { chosen = point; break; }
                if (distance > best) { best = distance; chosen = point; }
            }
            ZombieAgent template = bearChance && bearTemplate != null ? bearTemplate : bunnyTemplate;
            if (template == null || chosen == null) return;
            ZombieAgent enemy = Instantiate(template, chosen.position, chosen.rotation);
            enemy.gameObject.SetActive(true);
            enemy.Initialize(this, player);
            alive.Add(enemy);
        }

        void AddClueForWave(int completedWave)
        {
            if (zones == null || zones.Length == 0 || targetZone >= zones.Length) return;
            VaccineZone target = zones[targetZone];
            string clue;
            if (completedWave == 1)
            {
                int excluded = (targetZone + Mathf.Max(1, zones.Length / 2)) % zones.Length;
                clue = $"NO SIGNAL near {zones[excluded].displayName}. That zone is eliminated.";
            }
            else if (completedWave == 2) clue = $"A weak trace echoes around {target.landmark}.";
            else if (completedWave == 3)
            {
                int neighbor = (targetZone + 1) % zones.Length;
                clue = $"Search narrowed: {target.displayName} or {zones[neighbor].displayName}.";
            }
            else if (completedWave == 4) clue = $"Signal strength is highest in {target.displayName}.";
            else clue = $"CONFIRMED: Vaccine is hidden somewhere in {target.displayName}.";
            clues.Add(clue);
        }

        public void SelectReward(bool health)
        {
            if (Phase != GamePhase.Intermission || rewardChosen || player == null) return;
            rewardChosen = true;
            if (health) player.Heal(25); else player.UpgradeDefaultReserveAmmo(30);
        }

        void AutoReward()
        {
            if (player != null) SelectReward(player.Health < player.MaxHealth * 0.6f);
        }

        public void SelectLightZone(int index)
        {
            if (Phase != GamePhase.Intermission || zones == null || index < 0 || index >= zones.Length) return;
            selectedLight = index;
        }

        void ActivateSelectedLight()
        {
            SetAllZoneLights(false);
            if (selectedLight < 0 || zones == null || selectedLight >= zones.Length) return;
            zones[selectedLight].zoneLight.enabled = true;
            ActiveLightRemaining = poweredLightDuration;
        }

        void SetAllZoneLights(bool value)
        {
            ActiveLightRemaining = value ? poweredLightDuration : 0f;
            if (zones == null) return;
            foreach (VaccineZone zone in zones) if (zone != null && zone.zoneLight != null) zone.zoneLight.enabled = value;
        }

        public bool EnemiesCanAttack => Phase == GamePhase.WaveActive || Phase == GamePhase.FinalSearch;
        public void NotifyPlayerDied() => GameOver("PLAYER OVERRUN");
        public void Victory()
        {
            if (Phase == GamePhase.Victory) return;
            Phase = GamePhase.Victory;
            DespawnAllEnemies();
            SetAllZoneLights(false);
            LockCursor(false);
        }

        public void GameOver(string reason)
        {
            if (Phase == GamePhase.GameOver || Phase == GamePhase.Victory) return;
            FailureReason = reason;
            Phase = GamePhase.GameOver;
            DespawnAllEnemies();
            SetAllZoneLights(false);
            LockCursor(false);
        }

        void DespawnAllEnemies()
        {
            foreach (ZombieAgent enemy in alive) if (enemy != null) Destroy(enemy.gameObject);
            alive.Clear();
        }

        void Pause()
        {
            phaseBeforePause = Phase;
            Phase = GamePhase.Paused;
            Time.timeScale = 0f;
            LockCursor(false);
        }

        public void Resume()
        {
            if (Phase != GamePhase.Paused) return;
            Phase = phaseBeforePause;
            Time.timeScale = 1f;
            LockCursor(true);
        }

        public void Restart() { Time.timeScale = 1f; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 700f, 0.65f, 1.1f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            DrawHud(w, h);
            if (journalOpen) DrawJournal(w, h);
            if (Phase == GamePhase.Intermission) DrawIntermission(w, h);
            else if (Phase == GamePhase.Paused) DrawEndPanel(w, h, "PAUSED", string.Empty, true);
            else if (Phase == GamePhase.Victory) DrawEndPanel(w, h, "MISSION COMPLETE", "VACCINE RETRIEVED", false);
            else if (Phase == GamePhase.GameOver) DrawEndPanel(w, h, "MISSION FAILED", FailureReason, false);
        }

        void DrawHud(float w, float h)
        {
            GUI.Box(new Rect(18, 18, 410, 126), string.Empty);
            string phaseName = Phase == GamePhase.FinalSearch ? "FINAL SEARCH" : Phase.ToString().ToUpperInvariant();
            GUI.Label(new Rect(34, 28, 380, 28), $"LAST VACCINE  |  {phaseName}", hudStyle);
            if (player != null)
                GUI.Label(new Rect(34, 58, 380, 55), $"HP {player.Health:000}/{player.MaxHealth:000}    AMMO {player.Magazine:00}/{player.ReserveAmmo:000}\nWAVE {Mathf.Clamp(Wave, 1, 5)}/5   PHASE {FormatTime(PhaseTimeRemaining)}   MISSION {FormatTime(MissionTimeRemaining)}", hudStyle);
            string lightText = ActiveLightRemaining > 0f && selectedLight >= 0 ? $"LIGHT: {zones[selectedLight].displayName}  {ActiveLightRemaining:0}s" : "LIGHT: OFF";
            GUI.Label(new Rect(34, 112, 370, 24), lightText, hudStyle);
            GUI.Label(new Rect(w - 230, 20, 210, 54), "TAB: CLUE JOURNAL\nESC: PAUSE", hudStyle);
            GUI.Label(new Rect(w * 0.5f - 12, h * 0.5f - 18, 24, 36), "+", centerStyle);
            if (Phase == GamePhase.FinalSearch)
            {
                GUI.Label(new Rect(w * .5f - 300, 22, 600, 40), "VACCINE SIGNAL DETECTED — FIND THE VACCINE", titleStyle);
            }
        }

        void DrawIntermission(float w, float h)
        {
            float x = w * .5f - 360f;
            GUI.Box(new Rect(x, 150, 720, 470), string.Empty);
            GUI.Label(new Rect(x + 25, 166, 670, 45), $"WAVE {Wave} CLEAR  —  NEXT PHASE IN {Mathf.CeilToInt(PhaseTimeRemaining)}", titleStyle);
            string latest = clues.Count > 0 ? clues[clues.Count - 1] : "Signal analysis pending...";
            GUI.Label(new Rect(x + 35, 220, 650, 55), "NEW CLUE\n" + latest, clueStyle);
            GUI.Label(new Rect(x + 35, 295, 650, 28), rewardChosen ? "REWARD SELECTED" : "CHOOSE ONE REWARD", hudStyle);
            GUI.enabled = !rewardChosen;
            if (GUI.Button(new Rect(x + 35, 330, 300, 48), "+25 HP")) SelectReward(true);
            if (GUI.Button(new Rect(x + 385, 330, 300, 48), "+30 AMMO (PERMANENT)")) SelectReward(false);
            GUI.enabled = true;
            GUI.Label(new Rect(x + 35, 396, 650, 28), selectedLight >= 0 ? $"NEXT LIGHT: {zones[selectedLight].displayName}" : "SELECT ONE LIGHT ZONE (OPTIONAL)", hudStyle);
            for (int i = 0; zones != null && i < zones.Length; i++)
            {
                int row = i / 3, col = i % 3;
                Color old = GUI.backgroundColor;
                if (i == selectedLight) GUI.backgroundColor = new Color(.15f, .9f, 1f);
                if (GUI.Button(new Rect(x + 35 + col * 220, 432 + row * 55, 200, 42), $"{zones[i].id}: {zones[i].displayName}")) SelectLightZone(i);
                GUI.backgroundColor = old;
            }
            GUI.Label(new Rect(x + 35, 552, 650, 35), "The light timer begins only when the next combat phase starts.", clueStyle);
        }

        void DrawJournal(float w, float h)
        {
            GUI.Box(new Rect(w - 470, 90, 440, 330), string.Empty);
            GUI.Label(new Rect(w - 450, 105, 400, 34), "CLUE JOURNAL", titleStyle);
            string body = clues.Count == 0 ? "Survive a wave to decode the first clue." : string.Empty;
            for (int i = 0; i < clues.Count; i++) body += $"{i + 1}. {clues[i]}\n\n";
            GUI.Label(new Rect(w - 445, 150, 390, 250), body, clueStyle);
        }

        void DrawEndPanel(float w, float h, string headline, string subline, bool paused)
        {
            GUI.Box(new Rect(w * .5f - 280, h * .5f - 170, 560, 340), string.Empty);
            GUI.Label(new Rect(w * .5f - 250, h * .5f - 130, 500, 50), headline, titleStyle);
            GUI.Label(new Rect(w * .5f - 240, h * .5f - 72, 480, 35), subline, centerStyle);
            if (paused && GUI.Button(new Rect(w * .5f - 170, h * .5f - 10, 340, 48), "RESUME")) Resume();
            if (GUI.Button(new Rect(w * .5f - 170, h * .5f + 52, 340, 48), paused ? "RESTART" : "PLAY AGAIN")) Restart();
            if (GUI.Button(new Rect(w * .5f - 170, h * .5f + 112, 340, 38), "QUIT")) Quit();
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.4f, 1f, 1f) } };
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            centerStyle = new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            clueStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = new Color(.8f, .95f, 1f) } };
        }

        static string FormatTime(float seconds) => $"{Mathf.FloorToInt(Mathf.Max(0f, seconds) / 60f):00}:{Mathf.FloorToInt(Mathf.Max(0f, seconds) % 60f):00}";
        static void LockCursor(bool locked) { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
        static bool KeyDownEscape()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
        static bool KeyDownJournal()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }
    }

}
