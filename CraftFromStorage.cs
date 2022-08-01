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
using Mirror.RemoteCalls;
using Steamworks;
using TR;

// TODO: (POST RELEASE) Add UI Indicator to list # of items from chests vs player's inventory, mark which is first, and where it will be taken from
// TODO: (POST RELEASE) (OpenChestFromServer?) Make fully functional in multiplayer. It currently doesn't work at all for clients. (Works if you look in chest, but doesn't remove items)
// TODO: (POST RELEASE) Potentially add functionality in the deep mines (if enough people request)
// TODO: Figure out why cant do i.inside == house details

// BUG: (POST RELEASE) Removing items while in dialog with Franklyn (or Ted Selly) will cause item duplication (remove items right away, but restore if canceled)

namespace TR {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CraftFromStorage : BaseUnityPlugin {

        public const string pluginGuid = "tinyresort.dinkum.craftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.5.4";
        public static ManualLogSource StaticLogger;
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<int> nexusID;
        public static ConfigEntry<int> radius;
        public static ConfigEntry<bool> playerFirst;
        public static ConfigEntry<bool> isDebug;
        public static Vector3 currentPosition;
        public static List<ChestPlaceable> knownChests = new List<ChestPlaceable>();
        public static List<(Chest chest, HouseDetails house)> nearbyChests = new List<(Chest chest, HouseDetails house)>();
        public static bool usableTable;
        public static bool clientInServer;
        public static HouseDetails currentHouseDetails;
        public static Transform playerHouseTransform;
        public static bool isInside;
        public static HouseDetails playerHouse;

        public static Dictionary<int, InventoryItem> allItems = new Dictionary<int, InventoryItem>();
        public static bool allItemsInitialized;

        public static void Dbgl(string str = "") {
            if (isDebug.Value) { StaticLogger.LogInfo(str); }
        }

        private void Awake() {

            StaticLogger = Logger;

            #region Configuration

            nexusID = Config.Bind<int>("General", "NexusID", 28, "Nexus mod ID for updates");
            radius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            playerFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            isDebug = Config.Bind<bool>("General", "DebugMode", false, "Turns on debug mode. This makes it laggy when attempting to craft.");

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
            MethodInfo updateRWTL = AccessTools.Method(typeof(RealWorldTimeLight), "Update");

            MethodInfo fillRecipeIngredientsPatch = AccessTools.Method(typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            MethodInfo takeItemsForRecipePatch = AccessTools.Method(typeof(CraftFromStorage), "takeItemsForRecipePatch");
            MethodInfo populateCraftListPrefix = AccessTools.Method(typeof(CraftFromStorage), "populateCraftListPrefix");
            MethodInfo pressCraftButtonPrefix = AccessTools.Method(typeof(CraftFromStorage), "pressCraftButtonPrefix");
            MethodInfo closeCraftPopupPrefix = AccessTools.Method(typeof(CraftFromStorage), "closeCraftPopupPrefix");
            MethodInfo canBeCraftedPatch = AccessTools.Method(typeof(CraftFromStorage), "canBeCraftedPatch");
            MethodInfo craftItemPrefix = AccessTools.Method(typeof(CraftFromStorage), "craftItemPrefix");
            MethodInfo updatePrefix = AccessTools.Method(typeof(CraftFromStorage), "updatePrefix");
            MethodInfo updateRWTLPrefix = AccessTools.Method(typeof(CraftFromStorage), "updateRWTLPrefix");

            harmony.Patch(fillRecipeIngredients, new HarmonyMethod(fillRecipeIngredientsPatch));
            harmony.Patch(takeItemsForRecipe, new HarmonyMethod(takeItemsForRecipePatch));
            harmony.Patch(populateCraftList, new HarmonyMethod(populateCraftListPrefix));
            harmony.Patch(pressCraftButton, new HarmonyMethod(pressCraftButtonPrefix));
            harmony.Patch(closeCraftPopup, new HarmonyMethod(closeCraftPopupPrefix));
            harmony.Patch(canBeCrafted, new HarmonyMethod(canBeCraftedPatch));
            harmony.Patch(craftItem, new HarmonyMethod(craftItemPrefix));
            harmony.Patch(update, new HarmonyMethod(updatePrefix));
            harmony.Patch(updateRWTL, new HarmonyMethod(updateRWTLPrefix));

            #endregion

        }

        public static bool disableMod() {
            if (clientInServer || RealWorldTimeLight.time.underGround) return true;
            return false;
        }

        [HarmonyPrefix]
        private static void updateRWTLPrefix(RealWorldTimeLight __instance) {
            // Clients in a multiplayer world should not be able to craft from storage
            if (!__instance.isServer)
                clientInServer = true;
            else { clientInServer = false; }

        }

        private static void InitializeAllItems() {
            if (disableMod()) return;
            allItemsInitialized = true;
            foreach (var item in Inventory.inv.allItems) {
                var id = item.getItemId();
                allItems[id] = item;
            }

        }

        [HarmonyPrefix]
        public static void craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (disableMod()) return;
            ParseAllItems();
        }

        [HarmonyPrefix]
        public static bool pressCraftButtonPrefix(CraftingManager __instance, int ___currentVariation) {
            if (disableMod()) return true;

            // For checking if something was changed about the recipe items after opening recipe
            var wasCraftable = __instance.CraftButton.GetComponent<UnityEngine.UI.Image>().color == UIAnimationManager.manage.yesColor;

            ParseAllItems();
            __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, false);
            var craftable = __instance.canBeCrafted(__instance.craftableItemId);
            var showingRecipesFromMenu = (CraftingManager.CraftingMenuType)AccessTools.Field(typeof(CraftingManager), "showingRecipesFromMenu").GetValue(__instance);

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
            if (disableMod()) return;

            if (!__instance.craftWindowPopup.activeInHierarchy) return;
            ParseAllItems();
            __instance.updateCanBeCraftedOnAllRecipeButtons();

        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            if (disableMod()) return true;

            bool result = true;
            int num = Inventory.inv.allItems[itemId].value * 2;
            if (CharLevelManager.manage.checkIfUnlocked(__instance.craftableItemId) && Inventory.inv.allItems[itemId].craftable.workPlaceConditions != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
            if (Inventory.inv.wallet < num) { return false; }

            var recipe = ___currentVariation == -1 || Inventory.inv.allItems[itemId].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[itemId].craftable : Inventory.inv.allItems[itemId].craftable.altRecipes[___currentVariation];

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
            if (disableMod()) return true;
            var recipe = ___currentVariation == -1 || Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[currentlyCrafting].craftable : Inventory.inv.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }

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
                            info.sources[d].inPlayerHouse
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
            if (disableMod()) return;
            Inventory.inv.invSlots[slotID].stack = amountRemaining;
            Inventory.inv.invSlots[slotID].updateSlotContentsAndRefresh(amountRemaining == 0 ? -1 : itemID, amountRemaining);
        }

        [HarmonyPrefix]
        public static void populateCraftListPrefix() {
            if (disableMod()) return;
            ParseAllItems();
        }

        public static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
            if (disableMod()) return true;
            var recipe = variation == -1 || Inventory.inv.allItems[recipeNo].craftable.altRecipes.Length == 0 ? Inventory.inv.allItems[recipeNo].craftable : Inventory.inv.allItems[recipeNo].craftable.altRecipes[variation];
            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
                __instance.currentRecipeObjects.Add(UnityEngine.Object.Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
                __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                          .GetComponent<FillRecipeSlot>()
                          .fillRecipeSlotWithAmounts(
                               invItemId, GetItemCount(invItemId), recipe.stackOfItemsInRecipe[i]
                           );
            }
            return false;
        }

        [HarmonyPrefix]
        public static void updatePrefix(CharInteract __instance) {
            if (disableMod() || !__instance.isLocalPlayer) return;

            currentPosition = __instance.transform.position;

            if (Input.GetKeyDown(KeyCode.F12)) Dbgl($"Current Position: ({currentPosition.x}, {currentPosition.y}, {currentPosition.z}) | Underground: {RealWorldTimeLight.time.underGround}");
            currentHouseDetails = __instance.insideHouseDetails;
            playerHouseTransform = __instance.playerHouseTransform;
            isInside = __instance.insidePlayerHouse;

        }

        public static void FindNearbyChests() {
            if (disableMod()) return;
            nearbyChests.Clear();

            var chests = new List<(Collider hit, bool insideHouse)>();
            Collider[] chestsInsideHouse;
            Collider[] chestsOutside;
            int tempX, tempY;

            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }
            Dbgl($"{currentPosition.x} {0} {currentPosition.z}");
            chestsOutside = Physics.OverlapBox(new Vector3(currentPosition.x, -7, currentPosition.z), new Vector3(radius.Value * 2, 40, radius.Value * 2));
            Dbgl($"{currentPosition.x} {-88} {currentPosition.z}");
            chestsInsideHouse = Physics.OverlapBox(new Vector3(currentPosition.x, -88, currentPosition.z), new Vector3(radius.Value * 2, 5, radius.Value * 2));

            for (var i = 0; i < chestsInsideHouse.Length; i++) { chests.Add((chestsInsideHouse[i], true)); }
            for (var j = 0; j < chestsOutside.Length; j++) { chests.Add((chestsOutside[j], false)); }
            for (var k = 0; k < chests.Count; k++) {

                ChestPlaceable chestComponent = chests[k].hit.GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue;
                if (!knownChests.Contains(chestComponent)) { knownChests.Add(chestComponent); }

                var id = chestComponent.gameObject.GetInstanceID();
                var layer = chestComponent.gameObject.layer;
                var name = chestComponent.gameObject.name;
                var tag = chestComponent.gameObject.tag;

                // Dbgl($"ID: {id} | Layer: {layer} | Name: {name} | Tag: {tag}");

                tempX = chestComponent.myXPos();
                tempY = chestComponent.myYPos();

                if (chests[k].insideHouse) { ContainerManager.manage.checkIfEmpty(tempX, tempY, playerHouse); }
                else
                    ContainerManager.manage.checkIfEmpty(tempX, tempY, null);

                nearbyChests.Add((ContainerManager.manage.activeChests.First(i => i.xPos == tempX && i.yPos == tempY && i.inside == chests[k].insideHouse), playerHouse));
                nearbyChests = nearbyChests.Distinct().ToList();
            }
        }

        // Fills a dictionary with info about the items in player inventory and nearby chests
        public static void ParseAllItems() {

            if (disableMod()) return;
            if (!allItemsInitialized) { InitializeAllItems(); }

            // Recreate known chests and clear items
            FindNearbyChests();
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++) {
                if (Inventory.inv.invSlots[i].itemNo != -1 && allItems.ContainsKey(Inventory.inv.invSlots[i].itemNo))
                    AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, allItems[Inventory.inv.invSlots[i].itemNo].checkIfStackable(), null, null);
                else if (!allItems.ContainsKey(Inventory.inv.invSlots[i].itemNo)) { Dbgl($"Failed Item: {Inventory.inv.invSlots[i].itemNo} |  {Inventory.inv.invSlots[i].stack}"); }
            }

            // Get all items in nearby chests
            foreach (var ChestInfo in nearbyChests) {
                for (var i = 0; i < ChestInfo.chest.itemIds.Length; i++) {
                    if (ChestInfo.chest.itemIds[i] != -1 && allItems.ContainsKey(ChestInfo.chest.itemIds[i])) AddItem(ChestInfo.chest.itemIds[i], ChestInfo.chest.itemStacks[i], i, allItems[ChestInfo.chest.itemIds[i]].checkIfStackable(), ChestInfo.house, ChestInfo.chest);
                }
            }
        }

        public static void AddItem(int itemID, int quantity, int slotID, bool isStackable, HouseDetails isInHouse, Chest chest) {

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
            source.inPlayerHouse = isInHouse;

            if (chest == null) {
                source.playerInventory = true;
                info.sources.Insert(0, source);
                Dbgl($"Player Inventory -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity}");
            }
            else {
                info.sources.Add(source);
                Dbgl($"Chest Inventory{chest} -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity} | Chest X: {chest.xPos} | Chest Y: {chest.yPos}");
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
            public HouseDetails inPlayerHouse;
            public Chest chest;
        }

    }

}
