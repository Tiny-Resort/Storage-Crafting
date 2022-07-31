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
using Steamworks;
using TR;

// TODO: GUI element to tell us how many are in chest vs inventory (next to recipe)
// TODO: Work out kinks with being inside a house
// TODO: Add warning for someone taking items out. 
// TODO: Take items out as soon as craft button is pressed
// TODO: Fix multiplayer client not scanning for nearby chests
// TODO: Crafting Table in the mines loses cursor(?) make check to test if underground
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
        public static ConfigEntry<bool> playerFirst;
        public static Vector3 currentPosition;
        public static List<ChestPlaceable> knownChests = new List<ChestPlaceable>();
        public static List<Chest> nearbyChests = new List<Chest>();
        public static bool usableTable;
        public static int Sequence = 0;
        
        public static Dictionary<int, InventoryItem> allItems = new Dictionary<int, InventoryItem>();
        public static bool allItemsInitialized;

        public static bool isDebug = true;

        public static void Dbgl(string str = "") {
            if (isDebug) { StaticLogger.LogInfo(str); }
        }

        private void Awake() {

            StaticLogger = Logger;

            #region Configuration

            nexusID = Config.Bind<int>("General", "NexusID", 28, "Nexus mod ID for updates");
            radius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            playerFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            
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
            MethodInfo pressCraftButton = AccessTools.Method(typeof(CraftingManager), "pressCraftButton");
            MethodInfo closeCraftPopup = AccessTools.Method(typeof(CraftingManager), "closeCraftPopup");
            MethodInfo canBeCrafted = AccessTools.Method(typeof(CraftingManager), "canBeCrafted");
            MethodInfo craftItem = AccessTools.Method(typeof(CraftingManager), "craftItem");
            MethodInfo update = AccessTools.Method(typeof(CharInteract), "Update");


            MethodInfo fillRecipeIngredientsPatch = AccessTools.Method(typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            MethodInfo takeItemsForRecipePatch = AccessTools.Method(typeof(CraftFromStorage), "takeItemsForRecipePatch");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(CraftFromStorage), "populateCraftListPrefix");
            MethodInfo pressCraftButtonPrefix = AccessTools.Method(typeof(CraftFromStorage), "pressCraftButtonPrefix");
            MethodInfo closeCraftPopupPrefix = AccessTools.Method(typeof(CraftFromStorage), "closeCraftPopupPrefix");
            MethodInfo canBeCraftedPatch = AccessTools.Method(typeof(CraftFromStorage), "canBeCraftedPatch");
            MethodInfo craftItemPrefix = AccessTools.Method(typeof(CraftFromStorage), "craftItemPrefix");
            MethodInfo updatePrefix = AccessTools.Method(typeof(CraftFromStorage), "updatePrefix");
            
            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(takeItemsForRecipe, new HarmonyMethod(takeItemsForRecipePatch));
            harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));
            harmony.Patch(pressCraftButton, new HarmonyMethod(pressCraftButtonPrefix));
            harmony.Patch(closeCraftPopup, new HarmonyMethod(closeCraftPopupPrefix));
            harmony.Patch(canBeCrafted, new HarmonyMethod(canBeCraftedPatch));
            harmony.Patch(craftItem, new HarmonyMethod(craftItemPrefix));
            harmony.Patch(update, new HarmonyMethod(updatePrefix));

            #endregion
        }
        
        private static void InitializeAllItems() {
            allItemsInitialized = true;
            foreach (var item in Inventory.inv.allItems) {
                Dbgl($"InitializeAllItems {item}: {item.getItemId()}");

                var id = item.getItemId();
                allItems[id] = item;
            }
        }

        [HarmonyPrefix]
        public static void craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            ParseAllItems();
        }
        
        [HarmonyPrefix]
        public static bool pressCraftButtonPrefix(CraftingManager __instance, int ___currentVariation) {
        
            // For checking if something was changed about the recipe items after opening recipe
            var wasCraftable = __instance.CraftButton.GetComponent<UnityEngine.UI.Image>().color == UIAnimationManager.manage.yesColor;
                	
            ParseAllItems();
            __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, false);
            var craftable = __instance.canBeCrafted(__instance.craftableItemId);
            var showingRecipesFromMenu = (CraftingManager.CraftingMenuType) AccessTools.Field(typeof(CraftingManager), "showingRecipesFromMenu").GetValue(__instance);
        
            // If it can't be crafted, play a sound
        	if (!craftable) { 
        	    SoundManager.manage.play2DSound(SoundManager.manage.buttonCantPressSound); 
        	    if (wasCraftable) { Tools.Notify("Craft From Storage", "A required item was removed from your storage."); } // TODO: Show notification using TinyTools 
        	}
        	else if (showingRecipesFromMenu != CraftingManager.CraftingMenuType.CraftingShop &&
                     showingRecipesFromMenu != CraftingManager.CraftingMenuType.TrapperShop &&
                     !Inventory.inv.checkIfItemCanFit(__instance.craftableItemId, Inventory.inv.allItems[__instance.craftableItemId].craftable.recipeGiveThisAmount)) {
        		SoundManager.manage.play2DSound(SoundManager.manage.pocketsFull);
        		NotificationManager.manage.createChatNotification((LocalizedString)"ToolTips/Tip_PocketsFull", specialTip: true);
        	} 
        	
        	else {
        	    // TODO: Take items out of inventory before crafting, then somehow stop it from taking them out later 
        	    __instance.StartCoroutine(__instance.startCrafting(__instance.craftableItemId)); 
        	}
        	
            return false;
            
        }
        [HarmonyPrefix]
        public static void closeCraftPopupPrefix(CraftingManager __instance, CraftingManager.CraftingMenuType ___menuTypeOpen) {
            if (!__instance.craftWindowPopup.activeInHierarchy) return;
            //var sort = (Recipe.CraftingCatagory)AccessTools.Field(typeof(CraftingManager), "sortingBy").GetValue(__instance);
            //updateCraftingList(__instance);
            MethodInfo methodInfo = typeof(CraftingManager).GetMethod("populateCraftList", BindingFlags.NonPublic | BindingFlags.Instance);
            var parameters = new object[] { ___menuTypeOpen };
            methodInfo.Invoke(__instance, parameters);
        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {

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
                    break;
                }
            }

            __result = result;
            return false;
        }

        public static bool takeItemsForRecipePatch(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            var recipe = ___currentVariation == -1 || Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes.Length == 0 ? 
                                Inventory.inv.allItems[currentlyCrafting].craftable : 
                                Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
                int amountToRemove = recipe.stackOfItemsInRecipe[i];
                var info = GetItem(invItemId);
                info.sources = info.sources.OrderBy(b => b.fuel).ThenBy(b => b.playerInventory == playerFirst.Value ? 0 : 1).ToList();

                for (var d = 0; d < info.sources.Count; d++) {

                    // Cap out removed quantity at this source's quantity
                    var removed = Mathf.Min(amountToRemove, info.sources[d].quantity);

                    // If player inventory, remove from that
                    if (info.sources[d].playerInventory) { removeFromPlayerInventory(invItemId, info.sources[d].slotID, info.sources[d].quantity - removed); }

                    // If chest inventory, update that slot of the chest
                    else {
                        ContainerManager.manage.changeSlotInChest(
                            info.sources[d].chest.xPos,
                            info.sources[d].chest.yPos,
                            info.sources[d].slotID, 
                            removed >= info.sources[d].quantity ? -1 : invItemId,
                            info.sources[d].quantity - removed, 
                            null
                        );
                    }
                    amountToRemove -= removed;
                    if (amountToRemove <= 0) break;
                }
            }

            ParseAllItems();

            return false;
        }

        public static void removeFromPlayerInventory(int itemID, int slotID, int amountRemaining) {
            Inventory.inv.invSlots[slotID].stack = amountRemaining;
            Inventory.inv.invSlots[slotID].updateSlotContentsAndRefresh(amountRemaining == 0 ? -1 : itemID, amountRemaining);
        }
        
        [HarmonyPrefix]
        public static void populateCraftListPrefix() {
            ParseAllItems();
        }

        public static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
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
            return false;
        }

        [HarmonyPrefix]
        public static void updatePrefix(CharInteract __instance) {
            currentPosition = __instance.transform.position;
            if (Input.GetKeyDown(KeyCode.F12)) Dbgl($"Current Position: ({currentPosition.x}, {currentPosition.y}, {currentPosition.z})");
        }
        
        public static void FindNearbyChests() {
            nearbyChests.Clear();

            //var chests = Physics.OverlapBox(currentPosition, new Vector3(radius.Value, radius.Value, 7));
            var chests = Physics.OverlapSphere(currentPosition, radius.Value * 2, 15);
            int tempX, tempY;
            foreach (var hit in chests) {

                ChestPlaceable chestComponent = hit.GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue;
                if (!knownChests.Contains(chestComponent)) { knownChests.Add(chestComponent); }

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
        
            if (!allItemsInitialized) { InitializeAllItems(); }
        
            // Recreate known chests and clear items
            FindNearbyChests();
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++) {
                if (Inventory.inv.invSlots[i].itemNo != -1) AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, allItems[Inventory.inv.invSlots[i].itemNo].checkIfStackable(), null);
            }

            // Get all items in nearby chests
            foreach (var chest in nearbyChests) {
                for (var i = 0; i < chest.itemIds.Length; i++) {
                    if (chest.itemIds[i] != -1) AddItem(chest.itemIds[i], chest.itemStacks[i], i, allItems[chest.itemIds[i]].checkIfStackable(), chest);
                }
            }
        }

        public static void AddItem(int itemID, int quantity, int slotID, bool isStackable, Chest chest) {

            if (!nearbyItems.TryGetValue(itemID, out var info)) { info = new ItemInfo(); }
            ItemStack source = new ItemStack();
            
            if (!isStackable) {
                source.fuel = quantity;
                quantity = 1;
            }
            info.quantity += quantity;
                        
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
            public int fuel;
            public Chest chest;
        }

    }

}
