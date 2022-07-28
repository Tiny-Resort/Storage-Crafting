using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.Bootstrap;
using UnityEngine.InputSystem;


namespace TR {
    
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class StorageCrafting : BaseUnityPlugin {
        
        public const string pluginGuid = "tinyresort.dinkum.storagecrafting";
        public const string pluginName = "Storage Crafting";
        public const string pluginVersion = "0.1.0";
        public static ManualLogSource StaticLogger;
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<int> nexusID;

        

        public static bool isDebug = true;

        public static void Dbgl(string str = "") {
            if (isDebug) { StaticLogger.LogInfo(str); }
        }
        
        private void Awake() {

            StaticLogger = Logger;

            #region Configuration
            nexusID = Config.Bind<int>("General", "NexusID", 999, "Nexus mod ID for updates");
            #endregion

            #region Logging
            ManualLogSource logger = Logger;

            bool flag;
            BepInExInfoLogInterpolatedStringHandler handler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out flag);
            if (flag) { handler.AppendLiteral("Plugin " + pluginGuid + " (v" + pluginVersion + ") loaded!"); }
            logger.LogInfo(handler);
            #endregion

            #region Patching
            Harmony harmony = new Harmony(pluginGuid);

            /*MethodInfo update = AccessTools.Method(typeof(RealWorldTimeLight), "Update");
            MethodInfo updatePatch = AccessTools.Method(typeof(JournalPause), "updatePatch");
            
            harmony.Patch(update, new HarmonyMethod(updatePatch));*/

            #endregion

        }
    }

}
