using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "fillRecipeIngredients")]
internal class fillRecipeIngredients {

    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, int recipeNo, int variation) {
        if (CraftFromStorage.modDisabled) return true;
        var recipe = variation == -1 || Inventory.inv.allItems[recipeNo].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[recipeNo].craftable : Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation];
        for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
            int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
            __instance.currentRecipeObjects.Add(GameObject.Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
            __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                      .GetComponent<FillRecipeSlot>()
                      .fillRecipeSlotWithAmounts(
                           invItemId, CraftFromStorage.GetItemCount(invItemId), recipe.stackOfItemsInRecipe[i]
                       );
        }
        return false;
    }
}