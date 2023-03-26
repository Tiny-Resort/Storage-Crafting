using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "canBeCrafted")]
internal class canBeCrafted {
    
    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
        if (CraftFromStorage.modDisabled) return true;

        bool result = true;
        int num = Inventory.inv.allItems[itemId].value * 2;
        if (CharLevelManager.manage.checkIfUnlocked(__instance.craftableItemId) && Inventory.inv.allItems[itemId].craftable.workPlaceConditions != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
        if (Inventory.inv.wallet < num) { return false; }

        var recipe = ___currentVariation == -1 || Inventory.inv.allItems[itemId].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[itemId].craftable : Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation];

        for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
            int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
            int count = recipe.stackOfItemsInRecipe[i];
            if (CraftFromStorage.GetItemCount(invItemId) < count) {
                result = false;
                break;
            }
        }

        __result = result;
        return false;
    }
}