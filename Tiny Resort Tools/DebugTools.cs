using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity;
using UnityEngine.UI;

namespace TR {

    public class DebugTools : BaseUnityPlugin{
        #region Debug Logging

        public static bool isDebug;
        public static ManualLogSource StaticLogger;
        
        public static void DebugLog(string str) {
            if (isDebug) { StaticLogger.LogInfo(str); }
        }

        public static void DebugLog(int integer) {
            if (isDebug) { StaticLogger.LogInfo(integer); }
        }

        public static void DebugLog(string str, int integer) {
            if (isDebug) { StaticLogger.LogInfo(str + " " +  integer); }
        }

        public static void DebugLog<T>(List<T> list) {
            if (isDebug) {
                for (var i=0; i <= list.Count; i++) 
                {
                    StaticLogger.LogInfo($"Element {i} is {list[i]}");
                }
            }
        }
        
        #endregion
    }

}
