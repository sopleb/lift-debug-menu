using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.InputSystem;
using QFSW.QC;
using SaveSystem.Systems; // ConfigRange

namespace LiftDebug
{
    [BepInPlugin(Guid, "Lift Debug Menu", "0.5.0")]
    public class LiftDebugPlugin : BasePlugin
    {
        public const string Guid = "com.sopleb.liftdebug";
        internal static ManualLogSource Log;
        internal static CheatCommands Host; // injected MonoBehaviour that owns the command methods

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("Lift Debug Menu: loading...");
            ClassInjector.RegisterTypeInIl2Cpp<DebugController>();
            ClassInjector.RegisterTypeInIl2Cpp<CheatCommands>();
            AddComponent<DebugController>();

            // CheatCommands must live on a VISIBLE, persistent object so QC's FindObjectOfType
            // (MonoTargetType.Single) can locate it. BepInEx's own manager object is hidden.
            var host = new GameObject("LiftDebugHost");
            host.hideFlags = HideFlags.None;
            UnityEngine.Object.DontDestroyOnLoad(host);
            Host = host.AddComponent(Il2CppType.Of<CheatCommands>()).Cast<CheatCommands>();
            Log.LogInfo("Lift Debug Menu: loaded. F1 console | F2 noclip | F3 IsDebug | F4 diag. " +
                        "Console commands: give, items, noclip, tp, timescale.");
        }
    }

    // Injected IL2CPP MonoBehaviour: QC invokes these real methods on this real instance.
    public class CheatCommands : MonoBehaviour
    {
        public CheatCommands(IntPtr ptr) : base(ptr) { }

        public void GiveCount(string name, int count) => Cheats.Out(Cheats.Give(name, count));
        public void GiveOne(string name) => Cheats.Out(Cheats.Give(name, 1));
        public void ItemsFilter(string filter) => Cheats.Out(Cheats.ListItems(filter));
        public void ItemsAll() => Cheats.Out(Cheats.ListItems(null));
        public void NoClipCmd() => Cheats.Out(Cheats.NoClip());
        public void TpCmd(float x, float y, float z) => Cheats.Out(Cheats.Teleport(x, y, z));
        public void TimescaleCmd(float v) => Cheats.Out(Cheats.TimeScale(v));
    }

    // Native Quantum Console commands, bridged from managed delegates into the IL2CPP command table.
    internal static class Cheats
    {
        private static ManualLogSource Log => LiftDebugPlugin.Log;
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            try
            {
                var t = Il2CppType.Of<CheatCommands>();
                Add(t, "GiveCount", "give");
                Add(t, "GiveOne", "give");
                Add(t, "ItemsFilter", "items");
                Add(t, "ItemsAll", "items");
                Add(t, "NoClipCmd", "noclip");
                Add(t, "TpCmd", "tp");
                Add(t, "TimescaleCmd", "timescale");
                _registered = true;
                Log.LogInfo($"Registered cheat commands. Total commands now: {QuantumConsoleProcessor.LoadedCommandCount}.");
            }
            catch (Exception e) { Log.LogError($"Register failed: {e}"); }
        }

        private static void Add(Il2CppSystem.Type type, string methodName, string commandName)
        {
            var mi = type.GetMethod(methodName);
            if (mi == null) { Log.LogWarning($"Register: method '{methodName}' not found on injected type."); return; }
            var cmd = new CommandData(mi, commandName, MonoTargetType.Single, 0);
            if (!QuantumConsoleProcessor.TryAddCommand(cmd))
                Log.LogWarning($"TryAddCommand returned false for '{commandName}' ({methodName}).");
        }

        internal static void Out(string msg)
        {
            var qc = QuantumConsole.Instance;
            if (qc != null) qc.LogToConsole(msg ?? string.Empty, true);
            else Log.LogInfo(msg);
        }

        private static T FindFirst<T>() where T : UnityEngine.Object
        {
            var arr = Resources.FindObjectsOfTypeAll<T>();
            return arr != null && arr.Length > 0 ? arr[0] : null;
        }

        private static ItemDefinition FindItemDef(string name)
        {
            var dbs = Resources.FindObjectsOfTypeAll<ItemsDatabase>();
            if (dbs == null) return null;
            ItemDefinition contains = null;
            foreach (var db in dbs)
            {
                if (db == null) continue;
                var defs = db.GetAllItemDefinitions();
                if (defs == null) continue;
                foreach (var d in defs)
                {
                    if (d == null) continue;
                    var n = d.name;
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return d;
                    if (contains == null && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) contains = d;
                }
            }
            return contains;
        }

        // ---- command bodies ----

        internal static string Give(string name, int count)
        {
            if (string.IsNullOrWhiteSpace(name)) return "usage: give <name> [count]";
            count = Math.Max(1, count);
            var def = FindItemDef(name);
            if (def == null) return $"no item matching '{name}' (try: items {name})";
            var player = FindFirst<Player>();
            if (player == null) return "no player found (load a save first)";
            var item = new Item(def, count);
            player.AddItem(item, def.Prefab, default(ItemSlotType), default(ConfigRange), null, int.MinValue);
            return $"gave {count}x {def.name}";
        }

        internal static string ListItems(string filter)
        {
            var dbs = Resources.FindObjectsOfTypeAll<ItemsDatabase>();
            if (dbs == null) return "no ItemsDatabase found";
            var sb = new StringBuilder();
            int n = 0;
            foreach (var db in dbs)
            {
                if (db == null) continue;
                var defs = db.GetAllItemDefinitions();
                if (defs == null) continue;
                foreach (var d in defs)
                {
                    if (d == null) continue;
                    var nm = d.name;
                    if (!string.IsNullOrEmpty(filter) && nm.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb.Append(nm).Append("    ");
                    if (++n >= 80) { sb.Append("\n...(truncated, narrow the filter)"); goto done; }
                }
            }
        done:
            return n == 0 ? "no items matched" : $"{n} item(s):\n{sb}";
        }

        internal static string NoClip()
        {
            var player = FindFirst<Player>();
            if (player == null) return "no player found";
            player.ToggleNoClip();
            return $"noclip -> {player.NoClip}";
        }

        internal static string Teleport(float x, float y, float z)
        {
            var player = FindFirst<Player>();
            if (player == null) return "no player found";
            player.transform.position = new Vector3(x, y, z);
            return $"teleported to {x}, {y}, {z}";
        }

        internal static string TimeScale(float v)
        {
            Time.timeScale = Mathf.Max(0f, v);
            return $"timescale -> {Time.timeScale}";
        }
    }

    // Hotkeys + input suppression.
    public class DebugController : MonoBehaviour
    {
        public DebugController(IntPtr ptr) : base(ptr) { }
        private static ManualLogSource Log => LiftDebugPlugin.Log;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) ToggleConsole();
            if (kb.f2Key.wasPressedThisFrame) ToggleNoClip();
            if (kb.f3Key.wasPressedThisFrame) ForceDebug();
            if (kb.f4Key.wasPressedThisFrame) Diagnostics();
        }

        private static T FindFirst<T>() where T : UnityEngine.Object
        {
            var arr = Resources.FindObjectsOfTypeAll<T>();
            return arr != null && arr.Length > 0 ? arr[0] : null;
        }

        private void ToggleConsole()
        {
            try
            {
                var qc = QuantumConsole.Instance ?? FindFirst<QuantumConsole>();
                if (qc == null) { Log.LogWarning("No QuantumConsole found."); return; }
                if (qc.IsActive) CloseConsole(qc); else OpenConsole(qc);
            }
            catch (Exception e) { Log.LogError($"ToggleConsole failed: {e}"); }
        }

        private void OpenConsole(QuantumConsole qc)
        {
            if (!QuantumConsoleProcessor.TableGenerated)
            {
                try { QuantumConsoleProcessor.GenerateCommandTable(false, false); }
                catch (Exception e) { Log.LogWarning($"GenerateCommandTable failed: {e.Message}"); }
            }
            Cheats.Register(); // add our commands once the table exists
            qc._showPopupDisplay = true;
            qc._focusOnActivate = true;
            qc.Activate(true);
            SetGameInput(false);
            Log.LogInfo($"Console opened. Commands loaded: {QuantumConsoleProcessor.LoadedCommandCount}.");
        }

        private void CloseConsole(QuantumConsole qc)
        {
            qc.Deactivate();
            SetGameInput(true);
            Log.LogInfo("Console closed.");
        }

        private static void SetGameInput(bool on)
        {
            var arr = Resources.FindObjectsOfTypeAll<GameInput>();
            if (arr == null) return;
            foreach (var gi in arr)
                if (gi != null) gi.enabled = on;
        }

        private void ToggleNoClip()
        {
            try
            {
                var player = FindFirst<Player>();
                if (player == null) { Log.LogWarning("No Player found (are you in-game?)."); return; }
                player.ToggleNoClip();
                Log.LogInfo($"Toggled NoClip -> {player.NoClip}");
            }
            catch (Exception e) { Log.LogError($"ToggleNoClip failed: {e}"); }
        }

        private void ForceDebug()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<SystemsSettings>();
                if (all == null || all.Length == 0) { Log.LogWarning("No SystemsSettings found."); return; }
                foreach (var s in all)
                {
                    var cur = s._currentGameSettings; cur.IsDebug = true; s._currentGameSettings = cur;
                    var def = s.defaultGameSettings; def.IsDebug = true; s.defaultGameSettings = def;
                    Log.LogInfo($"Set IsDebug=true on '{s.name}'.");
                }
            }
            catch (Exception e) { Log.LogError($"ForceDebug failed: {e}"); }
        }

        private void Diagnostics()
        {
            try
            {
                var qc = Resources.FindObjectsOfTypeAll<QuantumConsole>();
                var players = Resources.FindObjectsOfTypeAll<Player>();
                var dbs = Resources.FindObjectsOfTypeAll<ItemsDatabase>();
                Log.LogInfo($"[diag] QuantumConsole={qc.Length} (Instance={(QuantumConsole.Instance != null)}) | " +
                            $"Player={players.Length} | ItemsDatabase={dbs.Length} | tableGen={QuantumConsoleProcessor.TableGenerated} cmds={QuantumConsoleProcessor.LoadedCommandCount}");
            }
            catch (Exception e) { Log.LogError($"Diagnostics failed: {e}"); }
        }
    }
}
