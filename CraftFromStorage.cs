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

// TODO: Add UI Indicator to list # of items from chests vs player's inventory, mark which is first, and where it will be taken from
// BUG: Removing items while in dialog with Franklyn (or Ted Selly) will cause item duplication (remove items right away, but restore if canceled)
// Known Limitations: Fletch's Tent isn't detected. This is due to Fletch's tent being on a different y-level, so we aren't hitting it with the collider. No plan to implement. 

namespace TinyResort {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CraftFromStorage : BaseUnityPlugin {

        private static CraftFromStorage instance;
        
        public static TRPlugin Plugin;
        public const string pluginGuid = "TinyResort.CraftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.8.0";
        
        public static ConfigEntry<int> radius;
        public static ConfigEntry<bool> playerFirst;
        
        public static Vector3 currentPosition;
        
        public static List<(Chest chest, HouseDetails house)> nearbyChests = new List<(Chest chest, HouseDetails house)>();
        private static Dictionary<(int xPos, int yPos), HouseDetails> tempChests = new Dictionary<(int xPos, int yPos), HouseDetails>();
        public static Dictionary<int, InventoryItem> allItems = new Dictionary<int, InventoryItem>();

        public static HouseDetails playerHouse;

        public static CraftingManager CraftingInstance;

        public static bool allItemsInitialized;
        public static bool clientInServer;
        private static bool openingCraftMenu;
        private static bool findingNearbyChests;

        public static bool modDisabled => RealWorldTimeLight.time.underGround;

        
        private void Awake() {

            Plugin = TRTools.Initialize(this, Logger, 28, pluginGuid, pluginName, pluginVersion);
            instance = this;

            #region Configuration
            radius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            playerFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            #endregion
            
            #region Patching
            Plugin.QuickPatch(typeof(CraftingManager), "fillRecipeIngredients", typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "takeItemsForRecipe", typeof(CraftFromStorage), "takeItemsForRecipePatch");
            Plugin.QuickPatch(typeof(CraftingManager), "populateCraftList", typeof(CraftFromStorage), "populateCraftListPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "pressCraftButton", typeof(CraftFromStorage), "pressCraftButtonPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "closeCraftPopup", typeof(CraftFromStorage), "closeCraftPopupPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "openCloseCraftMenu", typeof(CraftFromStorage), "openCloseCraftMenuPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "canBeCrafted", typeof(CraftFromStorage), "canBeCraftedPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "craftItem", typeof(CraftFromStorage), "craftItemPrefix", "craftItemPostfix");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "Update", typeof(CraftFromStorage), "updateRWTLPrefix");
            Plugin.QuickPatch(typeof(ChestWindow), "openChestInWindow", typeof(CraftFromStorage), "openChestInWindowPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "UserCode_TargetOpenChest", typeof(CraftFromStorage), null, "UserCode_TargetOpenChestPostfix");
            #endregion  
        }

        // Clients in a multiplayer world should not be able to craft from storage
        [HarmonyPrefix] public static void updateRWTLPrefix(RealWorldTimeLight __instance) {
            clientInServer = !__instance.isServer;
            if (Input.GetKeyDown(KeyCode.F12)) {
                ParseAllItems();
                instance.StopCoroutine(populateCraftListRoutine());
                instance.StartCoroutine(populateCraftListRoutine());
            }
        }

        private static void InitializeAllItems() {
            if (modDisabled) return;
            allItemsInitialized = true;
            foreach (var item in Inventory.inv.allItems) {
                var id = item.getItemId();
                allItems[id] = item;
            }

        }

        [HarmonyPrefix]
        public static void craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (modDisabled) return;
            // TODO: When crafting an item, it needs to wait until all items are updated before allowing the craft
            // TODO: After crafting the item update the number
            ParseAllItems();
        }

        // This will update the screen immediately after crafting an item. 
        [HarmonyPostfix]
        public static void craftItemPostfix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (modDisabled) return;
            ParseAllItems();
            __instance.showRecipeForItem(currentlyCrafting, ___currentVariation, false);
        }

        [HarmonyPrefix]
        public static bool pressCraftButtonPrefix(CraftingManager __instance, int ___currentVariation) {
            if (modDisabled) return true;

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
            if (modDisabled) return;

            if (!__instance.craftWindowPopup.activeInHierarchy) return;
            ParseAllItems();
            __instance.updateCanBeCraftedOnAllRecipeButtons();

        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            if (modDisabled) return true;

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
            if (modDisabled) return true;
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

            //ParseAllItems();

            return false;
        }

        public static void removeFromPlayerInventory(int itemID, int slotID, int amountRemaining) {
            if (modDisabled) return;
            Inventory.inv.invSlots[slotID].stack = amountRemaining;
            Inventory.inv.invSlots[slotID].updateSlotContentsAndRefresh(amountRemaining == 0 ? -1 : itemID, amountRemaining);
        }

        [HarmonyPrefix] public static void openCloseCraftMenuPrefix(bool isMenuOpen) { if (isMenuOpen) openingCraftMenu = true; }

        [HarmonyPrefix]
        public static bool populateCraftListPrefix(CraftingManager __instance) {
            if (modDisabled || !openingCraftMenu) return true;
            instance.StopAllCoroutines();
            instance.StartCoroutine(populateCraftListRoutine());
            openingCraftMenu = false;
            return false;
        }

        private static IEnumerator populateCraftListRoutine() {
            Plugin.LogToConsole("Populating Craft List");
            ParseAllItems();
            yield return new WaitUntil(() => !findingNearbyChests);
            CraftingManager.manage.populateCraftList();
        }

        public static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
            if (modDisabled) return true;
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

        public static bool openChestInWindowPrefix() {
            Plugin.LogToConsole($"Inside OPENCHESTINWINDOW AHHHHH | {findingNearbyChests}");
            return !findingNearbyChests;
        }

        public static void FindNearbyChests() { 
            
            nearbyChests.Clear();

            var chests = new List<(ChestPlaceable chest, bool insideHouse)>();
            Collider[] chestsInsideHouse;
            Collider[] chestsOutside;
            int tempX, tempY;

            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }

            currentPosition = NetworkMapSharer.share.localChar.myInteract.transform.position;
            
            chestsOutside = Physics.OverlapBox(new Vector3(currentPosition.x, -7, currentPosition.z), new Vector3(radius.Value * 2, 20, radius.Value * 2), Quaternion.identity, LayerMask.GetMask(LayerMask.LayerToName(15)));
            chestsInsideHouse = Physics.OverlapBox(new Vector3(currentPosition.x, -88, currentPosition.z), new Vector3(radius.Value * 2, 5, radius.Value * 2), Quaternion.identity, LayerMask.GetMask(LayerMask.LayerToName(15)));

            for (var i = 0; i < chestsInsideHouse.Length; i++) {
                ChestPlaceable chestComponent = chestsInsideHouse[i].GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue; 
                Plugin.LogToConsole("FOUND INSIDE HOUSE?: " + chestsInsideHouse[i].transform.position);
                Plugin.LogToConsole("COLLLIDER INFO: " + chestsInsideHouse[i].GetComponentInChildren<Collider>().bounds);
                chests.Add((chestComponent, true)); 
                
            }

            for (var j = 0; j < chestsOutside.Length; j++) {
                ChestPlaceable chestComponent = chestsOutside[j].GetComponentInParent<ChestPlaceable>();
                if (chestComponent == null) continue; 
                Plugin.LogToConsole("FOUND OUTSIDE HOUSE?: " + chestsOutside[j].transform.position);
                Plugin.LogToConsole("COLLLIDER INFO: " + chestsOutside[j].GetComponentInChildren<Collider>().bounds);
                chests.Add((chestComponent, false)); 
                
            }
            
            for (var k = 0; k < chests.Count; k++) {
                tempX = chests[k].chest.myXPos();
                tempY = chests[k].chest.myYPos();
    
                // TODO: Make this get the correct house --- I think playerHouse gets the current players house and not the hosts, it works for me, but you didnt see them...

                HouseDetails house = chests[k].insideHouse ? playerHouse : null;

                if (clientInServer) {
                    if (tempChests.ContainsKey((tempX, tempY))) { Plugin.LogToConsole("CHEST AT " + tempX + ", " + tempY + " already in dictionary"); }
                    tempChests[(tempX, tempY)] = house;
                    NetworkMapSharer.share.localChar.myPickUp.CmdOpenChest(tempX, tempY);
                    Plugin.LogToConsole("ADDING CHEST FROM " + tempX + ", " + tempY + ", " + (house != null));
                }
                
                else {
                    Plugin.LogToConsole("ADDING CHEST ON SERVER");
                    ContainerManager.manage.checkIfEmpty(tempX, tempY, house);
                    AddChest(tempX, tempY, house);
                }
            }
        }

        private static void AddChest(int xPos, int yPos, HouseDetails house) {
            Plugin.LogToConsole("Looking for chest " + xPos + ", " + yPos + ", " + (house != null));
            nearbyChests.Add((ContainerManager.manage.activeChests.First(i => i.xPos == xPos && i.yPos == yPos), house));
            nearbyChests = nearbyChests.Distinct().ToList();
        }
        
        [HarmonyPostfix]
        public static void UserCode_TargetOpenChestPostfix(ContainerManager __instance, int xPos, int yPos, int[] itemIds, int[] itemStack) {
            // TODO: Get proper house details
            if (tempChests.TryGetValue((xPos, yPos), out var house)) {
                Plugin.LogToConsole("CHEST OPENED FROM SERVER: " + xPos + ", " + yPos);
                tempChests.Remove((xPos, yPos));
                Plugin.LogToConsole("LATEST CHEST: " + ContainerManager.manage.activeChests[ContainerManager.manage.activeChests.Count - 1].xPos + ", " + 
                                    ContainerManager.manage.activeChests[ContainerManager.manage.activeChests.Count - 1].yPos);
                AddChest(xPos, yPos, house);
            }
        }
        // Fills a dictionary with info about the items in player inventory and nearby chests
        public static void ParseAllItems() { if (!modDisabled) instance.StartCoroutine(ParseAllItemsRoutine()); }
        
        public static IEnumerator ParseAllItemsRoutine() {
            
            if (!allItemsInitialized) { InitializeAllItems(); }

            // Recreate known chests and clear items
            findingNearbyChests = true;
            FindNearbyChests();
            if (clientInServer) { yield return new WaitUntil(() => tempChests.Count <= 0); }
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
