using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(NetworkMapSharer), "UserCode_RpcGiveOnTileStatus")]
internal class UserCode_RpcGiveOnTileStatus {

    [HarmonyPrefix]
    public static bool Prefix(ref int give, int xPos, int yPos) {
        return !(CraftFromStorage.findingNearbyChests); }        
}