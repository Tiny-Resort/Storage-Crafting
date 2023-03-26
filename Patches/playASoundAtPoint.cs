using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(SoundManager), "playASoundAtPoint")]
internal class playASoundAtPoint {

    [HarmonyPrefix]
    public static bool Prefix(SoundManager __instance, ASound soundToPlay) { return !((soundToPlay.name == "S_CrateOpens" || soundToPlay.name == "S_CrateClose" || soundToPlay.name == "S_CloseChest" || soundToPlay.name == "S_OpenChest") && CraftFromStorage.findingNearbyChests); }
}