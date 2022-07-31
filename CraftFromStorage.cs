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
            MethodInfo showRecipeForItem = AccessTools.Method(typeof(CraftingManager), "showRecipeForItem");
            MethodInfo populateCraftList = AccessTools.Method(typeof(CraftingManager), "populateCraftList");
            MethodInfo pressCraftButton = AccessTools.Method(typeof(CraftingManager), "pressCraftButton");
            MethodInfo closeCraftPopup = AccessTools.Method(typeof(CraftingManager), "closeCraftPopup");
            MethodInfo canBeCrafted = AccessTools.Method(typeof(CraftingManager), "canBeCrafted");
            MethodInfo craftItem = AccessTools.Method(typeof(CraftingManager), "craftItem");
            //MethodInfo pickUp = AccessTools.Method(typeof(CharPickUp), "pickUp");
            MethodInfo update = AccessTools.Method(typeof(CharInteract), "Update");


            MethodInfo fillRecipeIngredientsPatch = AccessTools.Method(typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            MethodInfo takeItemsForRecipePatch = AccessTools.Method(typeof(CraftFromStorage), "takeItemsForRecipePatch");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(CraftFromStorage), "populateCraftListPrefix");
            MethodInfo showRecipeForItemPrefix = AccessTools.Method(typeof(CraftFromStorage), "showRecipeForItemPrefix");
            MethodInfo pressCraftButtonPrefix = AccessTools.Method(typeof(CraftFromStorage), "pressCraftButtonPrefix");
            MethodInfo closeCraftPopupPrefix = AccessTools.Method(typeof(CraftFromStorage), "closeCraftPopupPrefix");
            MethodInfo canBeCraftedPatch = AccessTools.Method(typeof(CraftFromStorage), "canBeCraftedPatch");
            MethodInfo craftItemPrefix = AccessTools.Method(typeof(CraftFromStorage), "craftItemPrefix");
           // MethodInfo pickUpPatch = AccessTools.Method(typeof(CraftFromStorage), "pickUpPatch");
            MethodInfo updatePrefix = AccessTools.Method(typeof(CraftFromStorage), "updatePrefix");
            
            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(takeItemsForRecipe, new HarmonyMethod(takeItemsForRecipePatch));
            harmony.Patch(showRecipeForItem, new HarmonyMethod(showRecipeForItemPrefix));
            harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));
            harmony.Patch(pressCraftButton, new HarmonyMethod(pressCraftButtonPrefix));
            harmony.Patch(closeCraftPopup, new HarmonyMethod(closeCraftPopupPrefix));
            harmony.Patch(canBeCrafted, new HarmonyMethod(canBeCraftedPatch));
            harmony.Patch(craftItem, new HarmonyMethod(craftItemPrefix));
            harmony.Patch(update, new HarmonyMethod(updatePrefix));
            
            /*foreach (var item in Inventory.inv.allItems) {
                var id = item.getItemId();
                allItems[id] = item;
            }*/
            
            #endregion
        }
        
        private static void InitializeAllItems() {
            allItemsInitialized = true;
            foreach (var item in Inventory.inv.allItems) {
                var id = item.getItemId();
                allItems[id] = item;
            }
        }

        [HarmonyPrefix]
        public static void craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            Dbgl($"Craft Item Start: {++Sequence}");
            ParseAllItems();
            Dbgl($"Craft Item After RecipeForItem: {++Sequence}");
        }
        
        [HarmonyPrefix]
        public static bool pressCraftButtonPrefix(CraftingManager __instance, int ___currentVariation) {
        
            // For checking if something was changed about the recipe items after opening recipe
            var wasCraftable = __instance.CraftButton.GetComponent<UnityEngine.UI.Image>().color == UIAnimationManager.manage.yesColor;
                	
            Dbgl($"Press Craft Button: {++Sequence}");
            ParseAllItems();
            __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, false);
            var craftable = __instance.canBeCrafted(__instance.craftableItemId);
            var showingRecipesFromMenu = (CraftingManager.CraftingMenuType) AccessTools.Field(typeof(CraftingManager), "showingRecipesFromMenu").GetValue(__instance);
        
            // If it can't be crafted, play a sound
        	if (!craftable) { 
        	    SoundManager.manage.play2DSound(SoundManager.manage.buttonCantPressSound); 
        	    if (wasCraftable) {  } // TODO: Show notification using TinyTools 
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
            Dbgl($"Test: {++Sequence}");
            //updateCraftingList(__instance);
            MethodInfo methodInfo = typeof(CraftingManager).GetMethod("populateCraftList", BindingFlags.NonPublic | BindingFlags.Instance);
            var parameters = new object[] { ___menuTypeOpen };
            methodInfo.Invoke(__instance, parameters);
            Dbgl($"After Method Invoke: {++Sequence}");
        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            Dbgl($"Start canBeCraftedPatch: {++Sequence}");

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
                    Dbgl($"GetItemCount(invItemId) {invItemId} < count canBeCraftedPatch: {++Sequence}");
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

        [HarmonyPrefix]
        public static void populateCraftListPrefix() {
            Dbgl($"Before ParseAllItems of populateCraftListPrefix: {++Sequence}");

            ParseAllItems();
            Dbgl($"After ParseAllItems of populateCraftListPrefix: {++Sequence}");

        }
        
        [HarmonyPrefix]
        public static void showRecipeForItemPrefix() {
          Dbgl($"Start of showRecipeForItemPrefix: {++Sequence}");
        }
        
        public static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
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
        
            if (!allItemsInitialized) { InitializeAllItems(); }
        
            // Recreate known chests and clear items
            FindNearbyChests();
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++)
                AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, allItems[Inventory.inv.invSlots[i].itemNo].checkIfStackable(), null);

            // Get all items in nearby chests
            foreach (var chest in nearbyChests) {
                for (var i = 0; i < chest.itemIds.Length; i++)
                    AddItem(chest.itemIds[i], chest.itemStacks[i], i, allItems[chest.itemIds[i]].checkIfStackable(), chest);
            }
            
        }

        public static void AddItem(int itemID, int quantity, int slotID, bool isStackable, Chest chest) {

            if (!nearbyItems.TryGetValue(itemID, out var info)) { info = new ItemInfo(); }
            if (!isStackable) quantity = 1;
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
