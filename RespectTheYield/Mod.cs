using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using RespectTheYield.Helpers;
using RespectTheYield.Systems;
using System.Linq;
using System.Reflection;

namespace RespectTheYield
{
    public class Mod : IMod
    {
        public const string ModName = "RespectTheYield";
        public static Mod Instance { get; private set; }

        private Setting m_Setting;
        public Setting Setting => m_Setting;
        internal ILog Log { get; set; }
        private PrefixLogger m_Log;

        private Harmony m_harmony;
        private const string HarmonyId = "cs2.respectTheYield";

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;

            // Initialize logger.
            Log = LogManager
                  .GetLogger(ModName)
                  .SetShowsErrorsInUI(false);
#if IS_DEBUG
            Log = Log
                  .SetBacktraceEnabled(true)
                  .SetEffectiveness(Level.All);
#endif
            m_Log = new PrefixLogger(nameof(Mod));
            m_Log.Info($"Loading {ModName} version {Assembly.GetExecutingAssembly().GetName().Version}");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                m_Log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(RespectTheYield), m_Setting, new Setting(this));

            // Harmony
            m_harmony = new Harmony(HarmonyId);
            m_harmony.PatchAll(Assembly.GetExecutingAssembly());

            var patchedMethods = m_harmony.GetPatchedMethods().ToArray();
            m_Log.Debug($"Plugin {HarmonyId} made patches! Patched methods: " + patchedMethods);
            foreach (var patchedMethod in patchedMethods)
            {
                m_Log.Debug($"Patched: {patchedMethod.DeclaringType?.FullName}.{patchedMethod.Name}");
            }

            updateSystem.UpdateAt<YieldEnforcementSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            m_Log.Info("Disposing");
            m_harmony?.UnpatchAll(HarmonyId);
            m_Setting?.UnregisterInOptionsUI();
            m_Setting = null;
        }
    }
}
