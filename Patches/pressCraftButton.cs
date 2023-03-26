using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using I2.Loc;
using Mirror;
using Mirror.RemoteCalls;
using UnityEngine;
using UnityEngine.UI;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "pressCraftButton")]
internal class pressCraftButton {

    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, int ___currentVariation) {
        if (CraftFromStorage.modDisabled) return true;

        // For checking if something was changed about the recipe items after opening recipe
        var wasCraftable = __instance.CraftButton.GetComponent<Image>().color == UIAnimationManager.manage.yesColor;

        //Plugin.LogToConsole(++sequence + " PRESSING CRAFT BUTTON");
        CraftFromStorage.ParseAllItems();
        __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, false);
        var craftable = __instance.canBeCrafted(__instance.craftableItemId);
        var showingRecipesFromMenu = (CraftingManager.CraftingMenuType)AccessTools.Field(typeof(CraftingManager), "showingRecipesFromMenu").GetValue(__instance);

        // If it can't be crafted, play a sound
        if (!craftable) {
            SoundManager.manage.play2DSound(SoundManager.manage.buttonCantPressSound);
            if (wasCraftable) { TRTools.TopNotification("Craft From Storage", "CANCELED: A required item was removed from storage."); }
        }
        else if (showingRecipesFromMenu != CraftingManager.CraftingMenuType.CraftingShop &&
                 showingRecipesFromMenu != CraftingManager.CraftingMenuType.TrapperShop &&
                 !Inventory.inv.checkIfItemCanFit(__instance.craftableItemId, Inventory.inv.allItems[__instance.craftableItemId].craftable.recipeGiveThisAmount)) {
            SoundManager.manage.play2DSound(SoundManager.manage.pocketsFull);
            NotificationManager.manage.createChatNotification((LocalizedString)"ToolTips/Tip_PocketsFull", specialTip: true);
        }
        else { __instance.StartCoroutine(__instance.startCrafting(__instance.craftableItemId)); }
        return false;
    }
}