using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "populateCraftList")]
internal class populateCraftList {

    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, CraftingManager.CraftingMenuType listType) {
        if (CraftFromStorage.modDisabled || !CraftFromStorage.openingCraftMenu) return true;
        CraftFromStorage.openingCraftMenu = false;
        CraftFromStorage.OnFinishedParsing = () => CraftingManager.manage.populateCraftList(listType);
        CraftFromStorage.ParseAllItems();
        return false;
    }
}