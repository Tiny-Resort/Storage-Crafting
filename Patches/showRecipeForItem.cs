using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "showRecipeForItem")]
internal class showRecipeForItem {

    public static void Prefix(int recipeNo, int recipeVariation = -1, bool moveToAvaliableRecipe = true) {
        if (moveToAvaliableRecipe) {
            CraftFromStorage.OnFinishedParsing = () => CraftingManager.manage.showRecipeForItem(recipeNo, recipeVariation, false);
            CraftFromStorage.ParseAllItems();
        }
    }
}