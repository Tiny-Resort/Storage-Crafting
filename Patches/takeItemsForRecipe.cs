using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace TinyResort;

[HarmonyPatch(typeof(CraftingManager), "takeItemsForRecipe")]
internal class takeItemsForRecipe {

    [HarmonyPrefix]
    public static bool Prefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
        if (CraftFromStorage.modDisabled) return true;
        var recipe = ___currentVariation == -1 || Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[currentlyCrafting].craftable : Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation];

        for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
            if (HouseManager.manage.allHouses[i].isThePlayersHouse) { CraftFromStorage.playerHouse = HouseManager.manage.allHouses[i]; }
        }

        for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
            int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
            int amountToRemove = recipe.stackOfItemsInRecipe[i];
            var info = CraftFromStorage.GetItem(invItemId);

            info.sources = info.sources.OrderBy(b => b.fuel).ThenBy(b => b.playerInventory == CraftFromStorage.usePlayerInvFirst.Value ? 0 : 1).ToList();
            for (var d = 0; d < info.sources.Count; d++) {

                // Cap out removed quantity at this source's quantity
                var removed = Mathf.Min(amountToRemove, info.sources[d].quantity);

                // If player inventory, remove from that
                if (info.sources[d].playerInventory) { CraftFromStorage.removeFromPlayerInventory(invItemId, info.sources[d].slotID, info.sources[d].quantity - removed); }

                else {

                    // Remove from chest inventory on server
                    if (CraftFromStorage.clientInServer) {
                        NetworkMapSharer.share.localChar.myPickUp.CmdChangeOneInChest(
                            info.sources[d].chest.xPos,
                            info.sources[d].chest.yPos,
                            info.sources[d].slotID,
                            removed >= info.sources[d].quantity ? -1 : invItemId,
                            info.sources[d].quantity - removed
                        );
                    }

                    // Remove from chest inventory as host (or in single player)
                    else {
                        ContainerManager.manage.changeSlotInChest(
                            info.sources[d].chest.xPos,
                            info.sources[d].chest.yPos,
                            info.sources[d].slotID,
                            removed >= info.sources[d].quantity ? -1 : invItemId,
                            info.sources[d].quantity - removed,
                            info.sources[d].inPlayerHouse
                        );
                    }

                }

                // Remove from existing list of items as well
                info.sources[d].quantity -= removed;
                info.quantity -= removed;

                amountToRemove -= removed;
                if (amountToRemove <= 0) break;

            }

        }

        return false;
    }
}