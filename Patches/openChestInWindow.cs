using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(ChestWindow), "openChestInWindow")]
internal class openChestInWindow {

    [HarmonyPrefix]
    public static bool Prefix() {
        if (!CraftFromStorage.openChestWindow || CraftFromStorage.findingNearbyChests) { return false; }
        return true;
    }
}