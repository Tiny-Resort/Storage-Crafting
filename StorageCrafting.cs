using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using BepInEx.Unity.Bootstrap;
using UnityEngine.InputSystem;
using System.Runtime.Serialization.Formatters.Binary;
using I2.Loc;

namespace TR {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class StorageCrafting : BaseUnityPlugin {

        public const string pluginGuid = "tinyresort.dinkum.storagecrafting";
        public const string pluginName = "Storage Crafting";
        public const string pluginVersion = "0.1.0";
        public static ManualLogSource StaticLogger;
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<int> nexusID;
        public static Transform currentTransform;
        public static Transform testTransform;
        public static List<ChestPlaceable> knownChests = new List<ChestPlaceable>();
        public static Vector3 tableLocation;

        public static bool runOnce = true;

        public static bool isDebug = false;

        public static void Dbgl(string str = "") {
            if (isDebug) { StaticLogger.LogInfo(str); }
        }

        private void Awake() {

            StaticLogger = Logger;

            #region Configuration

            nexusID = Config.Bind<int>("General", "NexusID", 999, "Nexus mod ID for updates");

            #endregion

            #region Logging

            ManualLogSource logger = Logger;

            bool flag;
            BepInExInfoLogInterpolatedStringHandler handler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out flag);
            if (flag) { handler.AppendLiteral("Plugin " + pluginGuid + " (v" + pluginVersion + ") loaded!"); }
            logger.LogInfo(handler);

            #endregion

            #region Patching

            Harmony harmony = new Harmony(pluginGuid);

            MethodInfo fillRecipeIngredients = AccessTools.Method(typeof(CraftingManager), "fillRecipeIngredients");
            MethodInfo fillRecipeIngredientsPatch = AccessTools.Method(typeof(StorageCrafting), "fillRecipeIngredientsPatch");
            MethodInfo Update = AccessTools.Method(typeof(CharInteract), "Update");
            MethodInfo UpdatePatch = AccessTools.Method(typeof(StorageCrafting), "UpdatePatch");
            MethodInfo Start = AccessTools.Method(typeof(WorkTable), "Start");
            MethodInfo StartPatch = AccessTools.Method(typeof(StorageCrafting), "StartPatch");

            MethodInfo populateCraftList = AccessTools.Method(typeof(CraftingManager), "populateCraftList");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(StorageCrafting), "populateCraftListPrefix");

            MethodInfo canBeCrafted = AccessTools.Method(typeof(CraftingManager), "canBeCrafted");
            MethodInfo canBeCraftedPatch = AccessTools.Method(typeof(StorageCrafting), "canBeCraftedPatch");
            
            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(Update, new HarmonyMethod(UpdatePatch));
            harmony.Patch(Start, new HarmonyMethod(StartPatch));
            harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));
            harmony.Patch(canBeCrafted, new HarmonyMethod(canBeCraftedPatch));


            #endregion

        }

        /*public static bool showingToolTipPatch(InteractableObject __instance, Transform rayPos, CharPickUp myPickUp) {
            if (__instance.isWorkTable) {
                tableLocation.x = __instance.transform.localPosition.x;
                tableLocation.y = __instance.transform.localPosition.y;
                tableLocation.z = __instance.transform.localPosition.z;
            }
            return true;
        }*/

        public static bool StartPatch(WorkTable __instance) {
            testTransform = __instance.transform;
            return true;
        }

        public static bool UpdatePatch(CharInteract __instance) {
            
            currentTransform = __instance.transform;
            return true;
        }
        
        [HarmonyPatch]
        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            bool result = true;
            int num = Inventory.inv.allItems[itemId].value * 2;
            if (CharLevelManager.manage.checkIfUnlocked(__instance.craftableItemId) && Inventory.inv.allItems[itemId].craftable.workPlaceConditions != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
            if (Inventory.inv.wallet < num) { return false; }
            if (___currentVariation == -1 || Inventory.inv.allItems[itemId].craftable.altRecipes.Length == 0) {
                for (int i = 0; i < Inventory.inv.allItems[itemId].craftable.itemsInRecipe.Length; i++) {  
                    int invItemId = Inventory.inv.getInvItemId(Inventory.inv.allItems[itemId].craftable.itemsInRecipe[i]);
                    int num2 = Inventory.inv.allItems[itemId].craftable.stackOfItemsInRecipe[i];
                    if (Inventory.inv.getAmountOfItemInAllSlots(invItemId) + getAmountOfItemInAllSlotsInAllChests(invItemId) < num2) {
                        result = false;
                        break;
                    }
                }
            }
            else {
                for (int j = 0; j < Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation].itemsInRecipe.Length; j++) {
                    int invItemId2 = Inventory.inv.getInvItemId(Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation].itemsInRecipe[j]);
                    int num3 = Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation].stackOfItemsInRecipe[j];
                    if (Inventory.inv.getAmountOfItemInAllSlots(invItemId2) + getAmountOfItemInAllSlotsInAllChests(invItemId2) < num3) {
                        result = false;
                        break;
                    }
                }
            }
            __result = result;
            return false;
        }
        
        /* Patch this function to also take from the storage, but will probaby need to patch another function (or variable) to let it even detect that it can...
         * Make a variable that detects how much is in inventory and how much is in chests, remove from inventory first, and then remove rest from the chests
         * ContainerManager's function changeSlotInChest is the function that removes the items from slot and number to remove
         * public void changeSlotInChest(int xPos, int yPos, int slotNo, int newItemId, int newItemStack, HouseDetails inside)
         * Public funtion that can be called directly
         *
         * Need to patch CraftingManager.manage.canBeCrafted to check the storage containers and return true if we have enough materials*/
        /*public static bool takeItemsForRecipePatch(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (___currentVariation == -1) {
                for (int i = 0; i < Inventory.inv.allItems[currentlyCrafting].craftable.itemsInRecipe.Length; i++) {
                    int invItemId = Inventory.inv.getInvItemId(Inventory.inv.allItems[currentlyCrafting].craftable.itemsInRecipe[i]);
                    int amountToRemove = Inventory.inv.allItems[currentlyCrafting].craftable.stackOfItemsInRecipe[i];
                    Inventory.inv.removeAmountOfItem(invItemId, amountToRemove);
                    // ContainerManager.manage.changeSlotInChest();
                    // Add remove from storage here if amount isnt enough in inventory
                }
                return;
            }
            for (int j = 0; j < Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation].itemsInRecipe.Length; j++) {
                int invItemId2 = Inventory.inv.getInvItemId(Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation].itemsInRecipe[j]);
                int amountToRemove2 = Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation].stackOfItemsInRecipe[j];
                Inventory.inv.removeAmountOfItem(invItemId2, amountToRemove2);
                // ContainerManager.manage.changeSlotInChest();
                // Add remove from storage here if amount isnt enough in inventory
            }
        }*/
        /*public void craftItem(CraftingManager __instance, int currentlyCrafting, ref CraftingManager.CraftingMenuType ___showingRecipeFromMenu, int ___currentVariation) {
            int num = Inventory.inv.allItems[currentlyCrafting].value * 2;
            if (CharLevelManager.manage.checkIfUnlocked(currentlyCrafting) && ___showingRecipeFromMenu != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
            if (___showingRecipeFromMenu == CraftingManager.CraftingMenuType.CraftingShop) {
                if (NPCManager.manage.getVendorNPC(NPCSchedual.Locations.Craft_Workshop)) { NPCManager.manage.npcStatus[NPCManager.manage.getVendorNPC(NPCSchedual.Locations.Craft_Workshop).myId.NPCNo].moneySpentAtStore += Inventory.inv.allItems[currentlyCrafting].value; }
                return;
            }
            CraftingManager.CraftingMenuType craftingMenuType = ___showingRecipeFromMenu;
            __instance.takeItemsForRecipe(currentlyCrafting);
            Inventory.inv.changeWallet(-num, true);
            __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, true);
            if (Inventory.inv.allItems[currentlyCrafting].craftable.buildOnce) {
                __instance.setCraftOnlyOnceToFalse(currentlyCrafting);
                this.populateCraftList(CraftingManager.CraftingMenuType.PostOffice);
                __instance.RecipeWindow.gameObject.SetActive(false);
            }
            else {
                foreach (FillRecipeSlot fillRecipeSlot in __instance.recipeButtons) { fillRecipeSlot.refreshRecipeSlot(); }
            }
            if (Inventory.inv.allItems[currentlyCrafting].hasFuel) { Inventory.inv.addItemToInventory(currentlyCrafting, Inventory.inv.allItems[currentlyCrafting].fuelMax, true); }
            else if (___currentVariation == -1) { Inventory.inv.addItemToInventory(currentlyCrafting, Inventory.inv.allItems[currentlyCrafting].craftable.recipeGiveThisAmount, true); }
            else { Inventory.inv.addItemToInventory(currentlyCrafting, Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation].recipeGiveThisAmount, true); }
            SoundManager.manage.play2DSound(SoundManager.manage.craftingComplete);
        }*/

        [HarmonyPrefix]
        public static void populateCraftListPrefix(ContainerManager __instance, CraftingManager.CraftingMenuType listType = CraftingManager.CraftingMenuType.CraftingTable) {
            
            var chests = Physics.OverlapSphere(currentTransform.position, 20, 15);
            int tempX;
            int tempY;
            foreach (var hit in chests) {
                ChestPlaceable chestComponent = hit.GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue;
                if (!knownChests.Contains(chestComponent)) {
                    knownChests.Add(chestComponent);
                    for (var i = 0; i < knownChests.Count; i++) Dbgl($"Known Chests {i}: {knownChests[i]}");
                }
                var id = chestComponent.gameObject.GetInstanceID();
                var layer = chestComponent.gameObject.layer;
                var name = chestComponent.gameObject.name;
                var tag = chestComponent.gameObject.tag;
                Dbgl($"ID: {id} | Layer: {layer} | Name: {name} | Tag: {tag}");
                tempX = chestComponent.myXPos();
                tempY = chestComponent.myYPos();

                ContainerManager.manage.checkIfEmpty(tempX, tempY, null);
            }
        }
        
        private static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
            if (variation == -1) {
                for (int i = 0; i < Inventory.inv.allItems[recipeNo].craftable.itemsInRecipe.Length; i++) {
                    int invItemId = Inventory.inv.getInvItemId(Inventory.inv.allItems[recipeNo].craftable.itemsInRecipe[i]);
                    __instance.currentRecipeObjects.Add(UnityEngine.Object.Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
                    __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                              .GetComponent<FillRecipeSlot>()
                              .fillRecipeSlotWithAmounts(
                                   invItemId, Inventory.inv.getAmountOfItemInAllSlots(invItemId) + getAmountOfItemInAllSlotsInAllChests(invItemId), Inventory.inv.allItems[recipeNo].craftable.stackOfItemsInRecipe[i]
                               );
                    Dbgl($"Return Amount: {getAmountOfItemInAllSlotsInAllChests(invItemId)}");
                }
                return false;
            }
            for (int j = 0; j < Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation].itemsInRecipe.Length; j++) {
                int invItemId2 = Inventory.inv.getInvItemId(Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation].itemsInRecipe[j]);
                __instance.currentRecipeObjects.Add(UnityEngine.Object.Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
                __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                          .GetComponent<FillRecipeSlot>()
                          .fillRecipeSlotWithAmounts(
                               invItemId2, Inventory.inv.getAmountOfItemInAllSlots(invItemId2) + getAmountOfItemInAllSlotsInAllChests(invItemId2), Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation].stackOfItemsInRecipe[j]
                           );
            }
            return false;
        }

        public static int getAmountOfItemInAllSlotsInAllChests(int invItemId) {
            int amount = 0;
            for (int i = 0; i < ContainerManager.manage.activeChests.Count; i++) {
                for (int j = 0; j < ContainerManager.manage.activeChests[i].itemIds.Length; j++) {
                    // Add check if chest is in range and also active since chests get added to active forever
                    if (ContainerManager.manage.activeChests[i].itemIds[j] == invItemId) { amount += ContainerManager.manage.activeChests[i].itemStacks[j]; }
                    Dbgl($"Container {ContainerManager.manage.activeChests[i]} ItemID {j} {ContainerManager.manage.activeChests[i].itemIds[j]}");
                }
            }
            return amount;
        }

    }

}
