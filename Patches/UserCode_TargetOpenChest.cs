using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(ContainerManager), "UserCode_TargetOpenChest")]
internal class UserCode_TargetOpenChest {

    [HarmonyPostfix]
    public static void Postfix(ContainerManager __instance, int xPos, int yPos, int[] itemIds, int[] itemStack) {
        // TODO: Get proper house details
        if (CraftFromStorage.unconfirmedChests.TryGetValue((xPos, yPos), out var house)) {
            CraftFromStorage.numOfUnlockedChests += 1;
            CraftFromStorage.unconfirmedChests.Remove((xPos, yPos));
            CraftFromStorage.AddChest(xPos, yPos, house);
        }
    }
}