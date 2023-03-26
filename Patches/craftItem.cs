using HarmonyLib;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "craftItem")]
internal class craftItem {

    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
        if (CraftFromStorage.modDisabled) return true;

        // First time trying to craft an item, stall until we have up to date information
        if (!CraftFromStorage.tryingCraftItem) {
            CraftFromStorage.tryingCraftItem = true;
            CraftFromStorage.runCraftItemPostfix = false;

            //ToConsole(++sequence + " STARTING TO CRAFT ITEM");
            CraftFromStorage.OnFinishedParsing = () => CraftingManager.manage.craftItem(currentlyCrafting);
            CraftFromStorage.ParseAllItems();
            return false;
        }

        // With up to date information, check if we can still craft it, and if so continue. Otherwise, stop and tell the player.
        else {
            CraftFromStorage.tryingCraftItem = false;
            if (!__instance.canBeCrafted(__instance.craftableItemId)) {
                SoundManager.manage.play2DSound(SoundManager.manage.buttonCantPressSound);
                TRTools.TopNotification("Craft From Storage", "CANCELED: A required item was removed from storage.");

                //Plugin.LogToConsole(++sequence + " FAILED TO CRAFT ITEM");
                CraftFromStorage.runCraftItemPostfix = false;
                __instance.showRecipeForItem(currentlyCrafting, ___currentVariation, false);
                return false;
            }
            CraftFromStorage.runCraftItemPostfix = true;
            return true;
        }

    }

    // This will update the screen immediately after crafting an item. 
    [HarmonyPostfix]
    public static void Postfix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
        if (CraftFromStorage.modDisabled || !CraftFromStorage.runCraftItemPostfix) return;
        CraftFromStorage.runCraftItemPostfix = false;

        //Plugin.LogToConsole(++sequence + " CRAFT ITEM POSTFIX RUNNING");
        __instance.showRecipeForItem(currentlyCrafting, ___currentVariation, false);
    }
}