using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "closeCraftPopup")]
internal class closeCraftPopup {

    [HarmonyPrefix]
    public static void Prefix(CraftingManager __instance, CraftingManager.CraftingMenuType ___menuTypeOpen) {
        if (CraftFromStorage.modDisabled) return;
        if (!__instance.craftWindowPopup.activeInHierarchy) return;

        //Plugin.LogToConsole(++sequence + " CLOSING CRAFT POPUP");
        CraftFromStorage.OnFinishedParsing = () => __instance.updateCanBeCraftedOnAllRecipeButtons();
        CraftFromStorage.ParseAllItems();

    }
}