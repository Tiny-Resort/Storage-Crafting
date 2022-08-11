using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
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
using BepInEx.Unity;
using I2.Loc;
using Mirror.RemoteCalls;
using Steamworks;
using TinyResort;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

// TODO: (POST RELEASE) Add UI Indicator to list # of items from chests vs player's inventory, mark which is first, and where it will be taken from
// TODO: (POST RELEASE) (OpenChestFromServer?) Make fully functional in multiplayer. It currently doesn't work at all for clients. (Works if you look in chest, but doesn't remove items)
// TODO: (POST RELEASE) Potentially add functionality in the deep mines (if enough people request)
// TODO: Figure out why cant do i.inside == house details

// BUG: (POST RELEASE) Removing items while in dialog with Franklyn (or Ted Selly) will cause item duplication (remove items right away, but restore if canceled)

// Find nearby chests -> after finished running cmd open chest on ALL Chests dont due parse -> 
// -> wait until all chests have sent back info (how) (cap time)
// -> Parse all items on chests we have info about


namespace TinyResort {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CraftFromStorage : BaseUnityPlugin {

        public static TRPlugin Plugin;
        public const string pluginGuid = "games.tinyresort.craftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.5.4";
        
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<int> radius;
        public static ConfigEntry<bool> playerFirst;
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
        public static int sequence;
        public static CharPickUp charPickUp;
        public static CharInteract charInteract;
        public static bool findingNearbyChests;
        public Dictionary<string, PluginInfo> pluginInfos;
        public static bool duplicateModDetected;

        
        #region Mass Prefix to find info out
        [HarmonyPrefix]
        public static void closeChestFromServerPrefix() { Plugin.LogToConsole($"closeChestFromServerPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void openChestFromServerPrefix() { Plugin.LogToConsole($"openChestFromServerPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void openStashPrefix() { Plugin.LogToConsole($"openStashPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void playerCloseChestPrefix() { Plugin.LogToConsole($"playerCloseChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void playerOpenedChestPrefix() { Plugin.LogToConsole($"playerOpenedChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void getChestForWindowPrefix() { Plugin.LogToConsole($"getChestForWindowPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void changeSlotInChestPrefix() { Plugin.LogToConsole($"changeSlotInChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void clearWholeContainerPrefix() { Plugin.LogToConsole($"clearWholeContainerPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void RpcClearChestPrefix() { Plugin.LogToConsole($"RpcClearChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void RpcRefreshOpenedChestPrefix() { Plugin.LogToConsole($"RpcRefreshOpenedChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void TargetOpenChestPrefix() { Plugin.LogToConsole($"TargetOpenChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void EasySaveChestsPrefix() { Plugin.LogToConsole($"EasySaveChestsPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void EasyLoadChestExistsPrefix() { Plugin.LogToConsole($"EasyLoadChestExistsPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void EasyLoadChestsPrefix() { Plugin.LogToConsole($"EasyLoadChestsPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void saveChestPrefix() { Plugin.LogToConsole($"saveChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void saveStashesPrefix() { Plugin.LogToConsole($"saveStashesPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void loadStashesPrefix() { Plugin.LogToConsole($"loadStashesPrefix:  {++sequence}"); }

        [HarmonyPrefix]
        public static void InvokeUserCode_RpcClearChestPrefix() { Plugin.LogToConsole($"InvokeUserCode_RpcClearChest:  {++sequence}"); }

        [HarmonyPrefix]
        public static void InvokeUserCode_RpcRefreshOpenedChestPrefix() { Plugin.LogToConsole($"InvokeUserCode_RpcRefreshOpenedChest:  {++sequence}"); }

        [HarmonyPrefix]
        public static void InvokeUserCode_TargetOpenChestPrefix() { Plugin.LogToConsole($"InvokeUserCode_TargetOpenChestPrefix:  {++sequence}"); }

        [HarmonyPrefix]
        public static void UserCode_RpcClearChestPrefix() { Plugin.LogToConsole($"UserCode_RpcClearChest:  {++sequence}"); }

        [HarmonyPrefix]
        public static void UserCode_RpcRefreshOpenedChestPrefix() { Plugin.LogToConsole($"UserCode_RpcRefreshOpenedChest:  {++sequence}"); }

        [HarmonyPrefix]
        public static void UserCode_TargetOpenChestPrefix() { Plugin.LogToConsole($"UserCode_TargetOpenChestPrefix:  {++sequence}"); }
        [HarmonyPrefix]
        public static void checkIfEmptyPrefix() { Plugin.LogToConsole($"checkIfEmptyPrefix:  {++sequence}"); }

        #endregion
        private void Awake() {

            Plugin = TRTools.Initialize(this, Logger, 28, pluginGuid, pluginName, pluginVersion);

            #region Configuration
            radius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            playerFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            #endregion

            #region Temp Patching for Debugging
            Plugin.QuickPatch(typeof(ContainerManager), "closeChestFromServer", typeof(CraftFromStorage), "closeChestFromServerPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "openChestFromServer", typeof(CraftFromStorage), "openChestFromServerPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "openStash", typeof(CraftFromStorage), "openStashPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "playerCloseChest", typeof(CraftFromStorage), "playerCloseChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "playerOpenedChest", typeof(CraftFromStorage), "playerOpenedChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "getChestForWindow", typeof(CraftFromStorage), "getChestForWindowPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "changeSlotInChest", typeof(CraftFromStorage), "changeSlotInChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "openChestFromServer", typeof(CraftFromStorage), "openChestFromServerPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "clearWholeContainer", typeof(CraftFromStorage), "clearWholeContainerPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "RpcClearChest", typeof(CraftFromStorage), "RpcClearChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "RpcRefreshOpenedChest", typeof(CraftFromStorage), "RpcRefreshOpenedChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "TargetOpenChest", typeof(CraftFromStorage), "TargetOpenChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "EasySaveChests", typeof(CraftFromStorage), "EasySaveChestsPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "EasyLoadChestExists", typeof(CraftFromStorage), "EasyLoadChestExistsPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "EasyLoadChests", typeof(CraftFromStorage), "EasyLoadChestsPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "saveChest", typeof(CraftFromStorage), "saveChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "saveStashes", typeof(CraftFromStorage), "saveStashesPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "loadStashes", typeof(CraftFromStorage), "loadStashesPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "InvokeUserCode_TargetOpenChest", typeof(CraftFromStorage), "InvokeUserCode_TargetOpenChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "InvokeUserCode_RpcClearChest", typeof(CraftFromStorage), "InvokeUserCode_RpcClearChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "InvokeUserCode_RpcRefreshOpenedChest", typeof(CraftFromStorage), "InvokeUserCode_RpcRefreshOpenedChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "UserCode_TargetOpenChest", typeof(CraftFromStorage), "UserCode_TargetOpenChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "UserCode_RpcClearChest", typeof(CraftFromStorage), "UserCode_RpcClearChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "UserCode_RpcRefreshOpenedChest", typeof(CraftFromStorage), "UserCode_RpcRefreshOpenedChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "checkIfEmpty", typeof(CraftFromStorage), "checkIfEmptyPrefix");

            #endregion
            
            #region Patching
            Plugin.QuickPatch(typeof(CraftingManager), "fillRecipeIngredients", typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "takeItemsForRecipe", typeof(CraftFromStorage), "takeItemsForRecipePatch");
            Plugin.QuickPatch(typeof(CraftingManager), "populateCraftList", typeof(CraftFromStorage), "populateCraftListPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "pressCraftButton", typeof(CraftFromStorage), "pressCraftButtonPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "closeCraftPopup", typeof(CraftFromStorage), "closeCraftPopupPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "canBeCrafted", typeof(CraftFromStorage), "canBeCraftedPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "craftItem", typeof(CraftFromStorage), "craftItemPrefix");
            Plugin.QuickPatch(typeof(CharInteract), "Update", typeof(CraftFromStorage), "updatePrefix");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "Update", typeof(CraftFromStorage), "updateRWTLPrefix");
            Plugin.QuickPatch(typeof(CharPickUp), "Update", typeof(CraftFromStorage), "UpdateCharPickUpPrefix");
            Plugin.QuickPatch(typeof(CharInteract), "Update", typeof(CraftFromStorage), "UpdateCharInteractPrefix");
            Plugin.QuickPatch(typeof(ChestWindow), "openChestInWindow", typeof(CraftFromStorage), "openChestInWindowPrefix");

            #endregion

            pluginInfos = UnityChainloader.Instance.Plugins;
            foreach (KeyValuePair<string, PluginInfo> kvp in pluginInfos) {
                string pluginName = kvp.Value.Metadata.Name;
                string guid = kvp.Value.Metadata.GUID;
                if (guid == "tinyresort.dinkum.craftFromStorage") {
                    Plugin.LogToConsole($"Old version loaded");
                    duplicateModDetected = true;
                }
            }
            
        }

        public static bool disableMod() {
           // if (clientInServer || RealWorldTimeLight.time.underGround || duplicateModDetected) return true;
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
                if (wasCraftable) { TRTools.TopNotification("Craft From Storage", "A required item was removed from your storage."); }
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
                        if (clientInServer) {
                            NetworkMapSharer.share.localChar.myPickUp.CmdChangeOneInChest(
                                info.sources[d].chest.xPos,
                                info.sources[d].chest.yPos,
                                info.sources[d].slotID,
                                removed >= info.sources[d].quantity ? -1 : invItemId,
                                info.sources[d].quantity - removed
                            );
                        }
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

           // if (Input.GetKeyDown(KeyCode.F12)) Plugin.LogToConsole($"Current Position: ({currentPosition.x}, {currentPosition.y}, {currentPosition.z}) | Underground: {RealWorldTimeLight.time.underGround}");
            currentHouseDetails = __instance.insideHouseDetails;
            playerHouseTransform = __instance.playerHouseTransform;
            isInside = __instance.insidePlayerHouse;

        }

        public static bool openChestInWindowPrefix() {
            Plugin.LogToConsole($"Inside OPENCHESTINWINDOW AHHHHH | {findingNearbyChests}");
            return !findingNearbyChests;
        }
        
        [HarmonyPrefix]
        public static void UpdateCharPickUpPrefix(CharPickUp __instance) {
            charPickUp = __instance;
        }

        [HarmonyPrefix]
        public static void UpdateCharInteractPrefix(CharInteract __instance) { charInteract = __instance; } 

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
            chestsOutside = Physics.OverlapBox(new Vector3(currentPosition.x, -7, currentPosition.z), new Vector3(radius.Value * 2, 40, radius.Value * 2));
            chestsInsideHouse = Physics.OverlapBox(new Vector3(currentPosition.x, -88, currentPosition.z), new Vector3(radius.Value * 2, 5, radius.Value * 2));

            for (var i = 0; i < chestsInsideHouse.Length; i++) { chests.Add((chestsInsideHouse[i], true)); }
            for (var j = 0; j < chestsOutside.Length; j++) { chests.Add((chestsOutside[j], false)); }
            for (var k = 0; k < chests.Count; k++) {

                ChestPlaceable chestComponent = chests[k].hit.GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) {
                    continue;
                }
                if (!knownChests.Contains(chestComponent)) { knownChests.Add(chestComponent); }

                var id = chestComponent.gameObject.GetInstanceID();
                var layer = chestComponent.gameObject.layer;
                var name = chestComponent.gameObject.name;
                var tag = chestComponent.gameObject.tag;

                //Plugin.LogToConsole($"ID: {id} | Layer: {layer} | Name: {name} | Tag: {tag}");

                tempX = chestComponent.myXPos();
                tempY = chestComponent.myYPos();
    
                // TODO: Make this get the correct house
                var clientOrServer = !clientInServer ? ContainerManager.manage.connectionToClient : ContainerManager.manage.connectionToServer;
                HouseDetails tempDetails = chests[k].insideHouse ? playerHouse : null;

                // basically, do a for loop on CmdOpenChest on all chests then yield until we have info (half second), then add chests to nearbychest list
                ContainerManager.manage.checkIfEmpty(tempX, tempY, tempDetails);
                //NetworkMapSharer.share.localChar.myPickUp.CmdOpenChest(tempX, tempY);

                // make a for loop
                nearbyChests.Add((ContainerManager.manage.activeChests.First(i => i.xPos == tempX && i.yPos == tempY && i.inside == chests[k].insideHouse), tempDetails));
                nearbyChests = nearbyChests.Distinct().ToList();
            }
        }
        
        // Fills a dictionary with info about the items in player inventory and nearby chests
        public static void ParseAllItems() {

            if (disableMod()) return;
            if (!allItemsInitialized) { InitializeAllItems(); }

            // Recreate known chests and clear items
            findingNearbyChests = true;
            FindNearbyChests();
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++) {
                if (Inventory.inv.invSlots[i].itemNo != -1 && allItems.ContainsKey(Inventory.inv.invSlots[i].itemNo)) {
                    AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, allItems[Inventory.inv.invSlots[i].itemNo].checkIfStackable(), null, null);
                }
                else if (!allItems.ContainsKey(Inventory.inv.invSlots[i].itemNo)) { }
                //Plugin.LogToConsole($"Failed Item: {Inventory.inv.invSlots[i].itemNo} |  {Inventory.inv.invSlots[i].stack}"); }
            }

            // Get all items in nearby chests
            Plugin.LogToConsole($"Size of ChestInfo: {nearbyChests.Count}");
            foreach (var ChestInfo in nearbyChests) {
                for (var i = 0; i < ChestInfo.chest.itemIds.Length; i++) {
                    Plugin.LogToConsole($"Add Item: {ChestInfo.chest.itemIds[i]} |  {ChestInfo.chest.itemStacks[i]}");
                    if (ChestInfo.chest.itemIds[i] != -1 && allItems.ContainsKey(ChestInfo.chest.itemIds[i])) {
                        AddItem(ChestInfo.chest.itemIds[i], ChestInfo.chest.itemStacks[i], i, allItems[ChestInfo.chest.itemIds[i]].checkIfStackable(), ChestInfo.house, ChestInfo.chest);
                        Plugin.LogToConsole($"Add Item: {ChestInfo.chest.itemIds[i]} |  {ChestInfo.chest.itemStacks[i]}");
                    }
                }
            }
            findingNearbyChests = false;

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
               // Plugin.LogToConsole($"Player Inventory -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity}");
            }
            else {
                info.sources.Add(source);
              //  Plugin.LogToConsole($"Chest Inventory{chest} -- Slot ID: {slotID} | ItemID: {itemID} | Quantity: {source.quantity} | Chest X: {chest.xPos} | Chest Y: {chest.yPos}");
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

        public class FranklynItems {
            public int[] slotID;
            public int[] stack;
            public bool playerInventory;
            public Chest chest;
        }

    }
}
