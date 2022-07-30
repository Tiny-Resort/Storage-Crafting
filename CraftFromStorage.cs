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

// TODO: GUI element to tell us how many are in chest vs inventory (next to recipe)
// TODO: Work out kinks with being inside a house
// TODO: Add config to do player inventory last
// TODO: Re-check the storage contents when hitting craft --- DONE NEEDS TESTING
    // TODO: FOLLOWUP - Update the item number when trying to click and maybe give warning. 
// TODO: Take items out as soon as craft button is pressed
// TODO: Fix multiplayer client not scanning for nearby chests
// TODO: Crafting Table in the mines loses cursor(?) make check to test if underground
// TODO: Fix the game breaking when crafting from Franklyn (and maybe Irwin) (CraftingShop and TrapperShop) -- Bug requries a chest within range
// FIXED BUG: Items not showing up right away after you craft them. 
namespace TR {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CraftFromStorage : BaseUnityPlugin {

        public const string pluginGuid = "tinyresort.dinkum.craftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.5.0";
        public static ManualLogSource StaticLogger;
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<int> nexusID;
        public static ConfigEntry<int> radius;
        public static Transform currentTransform;
        public static List<ChestPlaceable> knownChests = new List<ChestPlaceable>();
        public static List<Chest> nearbyChests = new List<Chest>();
        public static bool usableTable;
        public static int Sequence = 0;

        public static bool isDebug = true;

        public static void Dbgl(string str = "") {
            if (isDebug) { StaticLogger.LogInfo(str); }
        }

        private void Awake() {

            StaticLogger = Logger;

            #region Configuration

            nexusID = Config.Bind<int>("General", "NexusID", 28, "Nexus mod ID for updates");
            radius = Config.Bind<int>("General", "Radius", 7, "Increases the range it looks for storage containers by an approximate tile count.");

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
            MethodInfo takeItemsForRecipe = AccessTools.Method(typeof(CraftingManager), "takeItemsForRecipe");
            MethodInfo populateCraftList = AccessTools.Method(typeof(CraftingManager), "populateCraftList");
            MethodInfo canBeCrafted = AccessTools.Method(typeof(CraftingManager), "canBeCrafted");
            MethodInfo pickUp = AccessTools.Method(typeof(CharPickUp), "pickUp");
            MethodInfo craftItem = AccessTools.Method(typeof(CraftingManager), "craftItem");


            MethodInfo fillRecipeIngredientsPatch = AccessTools.Method(typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            MethodInfo takeItemsForRecipePatch = AccessTools.Method(typeof(CraftFromStorage), "takeItemsForRecipePatch");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(CraftFromStorage), "populateCraftListPrefix");
            MethodInfo canBeCraftedPatch = AccessTools.Method(typeof(CraftFromStorage), "canBeCraftedPatch");
            MethodInfo pickUpPatch = AccessTools.Method(typeof(CraftFromStorage), "pickUpPatch");
            MethodInfo craftItemPrefix = AccessTools.Method(typeof(CraftFromStorage), "craftItemPrefix");

            
            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(takeItemsForRecipe, new HarmonyMethod(takeItemsForRecipePatch));
            //harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));
            harmony.Patch(canBeCrafted, new HarmonyMethod(canBeCraftedPatch));
            harmony.Patch(pickUp, new HarmonyMethod(pickUpPatch));
            harmony.Patch(craftItem, new HarmonyMethod(craftItemPrefix));

            
            #endregion
        }

        [HarmonyPrefix]
        public static void craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            Dbgl($"Craft Item Start: {++Sequence}");
            ParseAllItems();
            Dbgl($"Craft Item After RecipeForItem: {++Sequence}");
        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            Dbgl($"Start canBeCraftedPatch: {++Sequence}");

            ParseAllItems();
            Dbgl($"After ParseAllItems canBeCraftedPatch: {++Sequence}");

            bool result = true;
            int num = Inventory.inv.allItems[itemId].value * 2;
            if (CharLevelManager.manage.checkIfUnlocked(__instance.craftableItemId) && Inventory.inv.allItems[itemId].craftable.workPlaceConditions != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
            if (Inventory.inv.wallet < num) { return false; }

            var recipe = ___currentVariation == -1 || Inventory.inv.allItems[itemId].craftable.altRecipes.Length == 0 ? 
                                Inventory.inv.allItems[itemId].craftable : 
                                Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
                int count = recipe.stackOfItemsInRecipe[i];
                if (GetItemCount(invItemId) < count) {
                    result = false;
                    Dbgl($"GetItemCount(invItemId) < count canBeCraftedPatch: {++Sequence}");

                    break;
                }
            }
            Dbgl($"Before return False canBeCraftedPatch: {++Sequence}");

            __result = result;
            return false;
        }

        public static bool takeItemsForRecipePatch(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            Dbgl($"Start of takeItemsForRecipePatch: {++Sequence}");

            var recipe = ___currentVariation == -1 || Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes.Length == 0 ? 
                                Inventory.inv.allItems[currentlyCrafting].craftable : 
                                Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
                int amountToRemove = recipe.stackOfItemsInRecipe[i];

                var info = GetItem(invItemId);
                // TODO: Add config to do player inventory last
                //info.sources = info.sources.Reverse<ItemStack>().ToList<ItemStack>();

                foreach (var source in info.sources) {
                    // Cap out removed quantity at this source's quantity
                    var removed = Mathf.Min(amountToRemove, source.quantity);

                    // If player inventory, remove from that
                    if (source.playerInventory) {
                        Inventory.inv.removeAmountOfItem(invItemId, removed); }

                    // If chest inventory, update that slot of the chest
                    else {
                        ContainerManager.manage.changeSlotInChest(
                            source.chest.xPos, 
                            source.chest.yPos, 
                            source.slotID, 
                            removed >= source.quantity ? -1 : invItemId, 
                            source.quantity - removed, 
                            null
                        );
                    }
                    amountToRemove -= removed;
                    if (amountToRemove <= 0) break;
                }
            }
            Dbgl($"Before ParseAllItems of takeItemsForRecipePatch: {++Sequence}");

            ParseAllItems();
            Dbgl($"After ParseAllItems of takeItemsForRecipePatch: {++Sequence}");

            return false;
        }
        
        /* Gets the transform of the crafting table */
        [HarmonyPrefix]
        public static void pickUpPatch(CharPickUp __instance) {
            
            if (Physics.Raycast(__instance.transform.position + __instance.transform.forward * 1.5f + Vector3.up * 3f, 
                                Vector3.down, out var hitInfo2, 3.1f, __instance.pickUpLayerMask)) {
                WorkTable componentInParent5 = hitInfo2.transform.GetComponentInParent<WorkTable>();
                if ((bool)componentInParent5) {

                    if (componentInParent5.typeOfCrafting == CraftingManager.CraftingMenuType.CookingTable ||
                        componentInParent5.typeOfCrafting == CraftingManager.CraftingMenuType.CraftingTable) {
                        currentTransform = componentInParent5.transform;
                    } else Dbgl($"Type of Crafting Table: {componentInParent5.typeOfCrafting}");
                    Dbgl($"Work Table Name: {componentInParent5.workTableName}");
                    currentTransform = componentInParent5.transform;
                }
            }
        }

        [HarmonyPrefix]
        public static void populateCraftListPrefix() {
            Dbgl($"Before ParseAllItems of populateCraftListPrefix: {++Sequence}");

            ParseAllItems();
            Dbgl($"After ParseAllItems of populateCraftListPrefix: {++Sequence}");

        }
        
        private static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
            Dbgl($"Start of fillRecipeIngredientsPatch: {++Sequence}");
            var recipe = variation == -1 || Inventory.inv.allItems[recipeNo].craftable.altRecipes.Length == 0 ? 
                             Inventory.inv.allItems[recipeNo].craftable : 
                             Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation];

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
                __instance.currentRecipeObjects.Add(UnityEngine.Object.Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
                __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                            .GetComponent<FillRecipeSlot>()
                            .fillRecipeSlotWithAmounts(
                                invItemId, GetItemCount(invItemId), Inventory.inv.allItems[recipeNo].craftable.stackOfItemsInRecipe[i]
                            );
              //  Dbgl($"Running GetItemCount Method: {GetItemCount(invItemId)}");
            }
            Dbgl($"End of fillRecipeIngredientsPatch: {++Sequence}");
            return false;
        }

        public static void FindNearbyChests() {
            nearbyChests.Clear();
            var chests = Physics.OverlapSphere(currentTransform.position, radius.Value * 2, 15);
            int tempX, tempY;
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
               // Dbgl($"ID: {id} | Layer: {layer} | Name: {name} | Tag: {tag}");

                tempX = chestComponent.myXPos();
                tempY = chestComponent.myYPos();

                ContainerManager.manage.checkIfEmpty(tempX, tempY, null);
                nearbyChests.Add(ContainerManager.manage.activeChests.First(i => i.xPos == tempX && i.yPos == tempY));
            }
        }
        
        // Fills a dictionary with info about the items in player inventory and nearby chests
        public static void ParseAllItems() {
            FindNearbyChests();

            // Clear the existing dictionary
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++)
                AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, null);

            // Get all items in nearby chests
            foreach (var chest in nearbyChests) {
                for (var i = 0; i < chest.itemIds.Length; i++)
                    AddItem(chest.itemIds[i], chest.itemStacks[i], i, chest);
            }
        }

        public static void AddItem(int itemID, int quantity, int slotID, Chest chest) {

            if (!nearbyItems.TryGetValue(itemID, out var info)) { info = new ItemInfo(); }
            info.quantity += quantity;
            
            ItemStack source = new ItemStack();
            source.quantity += quantity;
            source.chest = chest;
            source.slotID = slotID;

           // Dbgl($"Radius: {radius.Value}");
            if (chest == null) { 
                source.playerInventory = true; 
                info.sources.Insert(0, source);
              //  Dbgl($"Player Inventory -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity}");
            } else {
                info.sources.Add(source);
              //  Dbgl($"Chest Inventory -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity} | Chest X: {chest.xPos} | Chest Y: {chest.yPos}");
            }
            nearbyItems[itemID] = info;

        }

        public static int GetItemCount(int itemID) => nearbyItems.TryGetValue(itemID, out var info) ? info.quantity : 0;

        public static ItemInfo GetItem(int itemID) => nearbyItems.TryGetValue(itemID, out var info) ? info : null;

        public static Dictionary<int, ItemInfo> nearbyItems = new Dictionary<int, ItemInfo>();

        public class ItemInfo {
            public int quantity;
            public List<ItemStack> sources = new List<ItemStack>();
        }

        public class ItemStack {
            public bool playerInventory;
            public int slotID;
            public int quantity;
            public Chest chest;
        }

    }

}
