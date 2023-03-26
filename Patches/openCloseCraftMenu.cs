using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "openCloseCraftMenu")]
internal class openCloseCraftMenu {

    [HarmonyPrefix] 
    public static void Prefix(bool isMenuOpen) {
        CraftFromStorage.CraftMenuIsOpen = isMenuOpen;
        if (isMenuOpen) { CraftFromStorage.openingCraftMenu = true; }
    }
}