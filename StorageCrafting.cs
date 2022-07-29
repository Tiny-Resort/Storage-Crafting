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
using BepInEx.Unity.Bootstrap;
using UnityEngine.InputSystem;
using System.Runtime.Serialization.Formatters.Binary;

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
        public static List<ChestPlaceable> knownChests = new List<ChestPlaceable>();

        public static bool runOnce = true;

        /* Notes for Crafting Storage:
         1. Crafting Manager:
            a. craftItem
                - Gets Item
                - Checks if item is unlocked & not the trapperShop (why)
                - If showing recipe from craftingshop do something
                - 
                i. this.takeItemsForRecipe(currentlyCrafting); -- modify to take items from chests too?
                ii. changeWallet???
                iii.  
         
         2. PopulateCraftList
            - shouldnt need to touch? Looks like just populates and doesnt change amounts of required items
         */

        public static bool isDebug = true;

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

            MethodInfo populateCraftList = AccessTools.Method(typeof(CraftingManager), "populateCraftList");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(StorageCrafting), "populateCraftListPrefix");
            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(Update, new HarmonyMethod(UpdatePatch));
            harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));

            #endregion

        }

        public static bool UpdatePatch(CharInteract __instance) {

            currentTransform = __instance.transform;

            return true;
        }

        [HarmonyPrefix]
        public static void populateCraftListPrefix(ContainerManager __instance, CraftingManager.CraftingMenuType listType = CraftingManager.CraftingMenuType.CraftingTable) {

            MethodInfo methodInfo = typeof(ContainerManager).GetMethod("getChestSaveOrCreateNewOne", BindingFlags.NonPublic);
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

                // crashes game
                //__instance.openChestFromServer(__instance.connectionToServer, tempX, tempY, null); /*


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

        public void Update() {
            /*var chests = Physics.OverlapSphere(currentTransform.position, 5, 15);
            foreach (var hit in chests) {
                ChestPlaceable chestComponent = hit.GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue;
                if (!knownChests.Contains(chestComponent)) {
                    knownChests.Add(chestComponent);
                    for (var i = 0; i < knownChests.Count; i++ )
                        Dbgl($"Known Chests {i}: {knownChests[i]}");
                }
                var id = chestComponent.gameObject.GetInstanceID();
                var layer = chestComponent.gameObject.layer;
                var name = chestComponent.gameObject.name;
                var tag = chestComponent.gameObject.tag;
                chestComponent.myXPos();
                chestComponent.myYPos();
            }*/

            /*for (int i = 0; i < ContainerManager.manage.activeChests.Count; i++) {
                for (int j = 0; j < ContainerManager.manage.activeChests[i].itemIds.Length; j++) {
                    Dbgl($"Container {ContainerManager.manage.} ItemID {j} {ContainerManager.manage.activeChests[i].itemIds[j]}");
                }
            }*/
        }
    }

}
