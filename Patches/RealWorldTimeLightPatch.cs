using HarmonyLib;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(RealWorldTimeLight), "Update")]

internal class RealWorldTimeLightPatch {
    
    [HarmonyPrefix] internal static void Prefix(RealWorldTimeLight __instance) { CraftFromStorage.clientInServer = !__instance.isServer; }
}
