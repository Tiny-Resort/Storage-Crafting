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

// TODO (LOW): Ingredient list is bouncy when opening menu.
// TODO (LOW): If you can't craft, someone puts item in chest, and you try to craft again, it takes two tries to recognize new items. ONLY HAPPENS FOR CLIENTS.
// TODO (MEDIUM): Host of a server and other clients will hear sound effect of chests opening and closing every time the someone is doing stuff at the crafting table.
// TODO (MEDIUM): Unsure how well chests being in vs out of a house functions
// BUG (MEDIUM): Removing items while in dialog with Franklyn (or Ted Selly) will cause item duplication (remove items right away, but restore if canceled)
// Known Limitations (NOT PLANNED): Fletch's Tent isn't detected. This is due to Fletch's tent being on a different y-level, so we aren't hitting it with the collider. No plan to implement. 

namespace TinyResort {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class CraftFromStorage : BaseUnityPlugin {

        private static CraftFromStorage instance;

        public static TRPlugin Plugin;
        public const string pluginGuid = "tinyresort.dinkum.craftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.8.2";

        public delegate void ParsingEvent();
        public static ParsingEvent OnFinishedParsing;

        public static ConfigEntry<int> tileRadius;
        public static ConfigEntry<bool> usePlayerInvFirst;

        public static Vector3 playerPosition;

        public static List<(Chest chest, HouseDetails house)> nearbyChests = new List<(Chest chest, HouseDetails house)>();
        private static Dictionary<(int xPos, int yPos), HouseDetails> unconfirmedChests = new Dictionary<(int xPos, int yPos), HouseDetails>();

        public static HouseDetails playerHouse;

        public static bool clientInServer;
        private static bool openingCraftMenu;
        private static bool CraftMenuIsOpen;
        private static bool tryingCraftItem;
        private static bool runCraftItemPostfix;
        private static bool findingNearbyChests;
        private static bool openChestWindow;

        public static bool ClientInsideHouse => NetworkMapSharer.Instance.localChar.myInteract._isInsidePlayerHouse && clientInServer;
        public static bool ClientOutsideHouse => !NetworkMapSharer.Instance.localChar.myInteract._isInsidePlayerHouse && clientInServer;

        public static bool modDisabled => RealWorldTimeLight.time.underGround;

        public static int sequence;
        public static int numOfUnlockedChests;

        private void Awake() {

            Plugin = TRTools.Initialize(this, 28);
            instance = this;

            #region Configuration

            tileRadius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            usePlayerInvFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            #endregion

            #region Patching

            Plugin.QuickPatch(typeof(CraftingManager), "fillRecipeIngredients", typeof(CraftFromStorage), "fillRecipeIngredientsPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "takeItemsForRecipe", typeof(CraftFromStorage), "takeItemsForRecipePatch");
            Plugin.QuickPatch(typeof(CraftingManager), "populateCraftList", typeof(CraftFromStorage), "populateCraftListPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "pressCraftButton", typeof(CraftFromStorage), "pressCraftButtonPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "showRecipeForItem", typeof(CraftFromStorage), "showRecipeForItemPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "closeCraftPopup", typeof(CraftFromStorage), "closeCraftPopupPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "openCloseCraftMenu", typeof(CraftFromStorage), "openCloseCraftMenuPrefix");
            Plugin.QuickPatch(typeof(CraftingManager), "canBeCrafted", typeof(CraftFromStorage), "canBeCraftedPatch");
            Plugin.QuickPatch(typeof(CraftingManager), "craftItem", typeof(CraftFromStorage), "craftItemPrefix", "craftItemPostfix");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "Update", typeof(CraftFromStorage), "updateRWTLPrefix");
            Plugin.QuickPatch(typeof(ChestWindow), "openChestInWindow", typeof(CraftFromStorage), "openChestInWindowPrefix");

            //Plugin.QuickPatch(typeof(ContainerManager), "playerOpenedChest", typeof(CraftFromStorage), "playerOpenedChestPrefix");
            Plugin.QuickPatch(typeof(ContainerManager), "TargetOpenChest", typeof(CraftFromStorage), "TargetOpenChestPrefix");

            Plugin.QuickPatch(typeof(ContainerManager), "UserCode_TargetOpenChest", typeof(CraftFromStorage), null, "UserCode_TargetOpenChestPostfix");

            //Plugin.QuickPatch(typeof(ShowObjectOnStatusChange), "showGameObject", typeof(CraftFromStorage), "showGameObjectPrefix");
            Plugin.QuickPatch(typeof(SoundManager), "playASoundAtPoint", typeof(CraftFromStorage), "playASoundAtPointPrefix");

            #endregion

        }

        // Clients in a multiplayer world should not be able to craft from storage
        [HarmonyPrefix] public static void updateRWTLPrefix(RealWorldTimeLight __instance) { clientInServer = !__instance.isServer; }

        public static bool craftItemPrefix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (modDisabled) return true;

            // First time trying to craft an item, stall until we have up to date information
            if (!tryingCraftItem) {
                tryingCraftItem = true;
                runCraftItemPostfix = false;

                //ToConsole(++sequence + " STARTING TO CRAFT ITEM");
                OnFinishedParsing = () => CraftingManager.manage.craftItem(currentlyCrafting);
                ParseAllItems();
                return false;
            }

            // With up to date information, check if we can still craft it, and if so continue. Otherwise, stop and tell the player.
            else {
                tryingCraftItem = false;
                if (!__instance.canBeCrafted(__instance.craftableItemId)) {
                    SoundManager.Instance.play2DSound(SoundManager.Instance.buttonCantPressSound);
                    TRTools.TopNotification("Craft From Storage", "CANCELED: A required item was removed from storage.");

                    //Plugin.LogToConsole(++sequence + " FAILED TO CRAFT ITEM");
                    runCraftItemPostfix = false;
                    __instance.showRecipeForItem(currentlyCrafting, ___currentVariation, false);
                    return false;
                }
                runCraftItemPostfix = true;
                return true;
            }

        }

        // This will update the screen immediately after crafting an item. 
        [HarmonyPostfix]
        public static void craftItemPostfix(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (modDisabled || !runCraftItemPostfix) return;
            runCraftItemPostfix = false;

            //Plugin.LogToConsole(++sequence + " CRAFT ITEM POSTFIX RUNNING");
            __instance.showRecipeForItem(currentlyCrafting, ___currentVariation, false);
        }

        public static bool showGameObjectPrefix(ShowObjectOnStatusChange __instance, int xPos, int yPos, HouseDetails inside = null) { return !__instance.isChest || !findingNearbyChests; }

        public static bool playASoundAtPointPrefix(SoundManager __instance, ASound soundToPlay) { return !((soundToPlay.name == "S_CrateOpens" || soundToPlay.name == "S_CrateClose" || soundToPlay.name == "S_CloseChest" || soundToPlay.name == "S_OpenChest") && findingNearbyChests); }

        public static void showRecipeForItemPrefix(int recipeNo, int recipeVariation = -1, bool moveToAvaliableRecipe = true) {
            if (moveToAvaliableRecipe) {
                OnFinishedParsing = () => CraftingManager.manage.showRecipeForItem(recipeNo, recipeVariation, false);
                ParseAllItems();
            }
        }

        public static bool pressCraftButtonPrefix(CraftingManager __instance, int ___currentVariation) {
            if (modDisabled) return true;

            // For checking if something was changed about the recipe items after opening recipe
            var wasCraftable = __instance.CraftButton.GetComponent<Image>().color == UIAnimationManager.manage.yesColor;

            //Plugin.LogToConsole(++sequence + " PRESSING CRAFT BUTTON");
            ParseAllItems();
            __instance.showRecipeForItem(__instance.craftableItemId, ___currentVariation, false);
            var craftable = __instance.canBeCrafted(__instance.craftableItemId);
            var showingRecipesFromMenu = (CraftingManager.CraftingMenuType)AccessTools.Field(typeof(CraftingManager), "showingRecipesFromMenu").GetValue(__instance);

            // If it can't be crafted, play a sound
            if (!craftable) {
                SoundManager.Instance.play2DSound(SoundManager.Instance.buttonCantPressSound);
                if (wasCraftable) { TRTools.TopNotification("Craft From Storage", "CANCELED: A required item was removed from storage."); }
            }
            else if (showingRecipesFromMenu != CraftingManager.CraftingMenuType.CraftingShop &&
                     showingRecipesFromMenu != CraftingManager.CraftingMenuType.TrapperShop &&
                     !Inventory.Instance.checkIfItemCanFit(__instance.craftableItemId, Inventory.Instance.allItems[__instance.craftableItemId].craftable.recipeGiveThisAmount)) {
                SoundManager.Instance.play2DSound(SoundManager.Instance.pocketsFull);
                NotificationManager.manage.createChatNotification((LocalizedString)"ToolTips/Tip_PocketsFull", specialTip: true);
            }
            else { __instance.StartCoroutine(__instance.startCrafting(__instance.craftableItemId)); }
            return false;
        }

        [HarmonyPrefix]
        public static void closeCraftPopupPrefix(CraftingManager __instance, CraftingManager.CraftingMenuType ___menuTypeOpen) {
            if (modDisabled) return;
            if (!__instance.craftWindowPopup.activeInHierarchy) return;

            //Plugin.LogToConsole(++sequence + " CLOSING CRAFT POPUP");
            OnFinishedParsing = () => __instance.updateCanBeCraftedOnAllRecipeButtons();
            ParseAllItems();

        }

        public static bool canBeCraftedPatch(CraftingManager __instance, int itemId, int ___currentVariation, ref bool __result) {
            if (modDisabled) return true;

            bool result = true;
            int num = Inventory.Instance.allItems[itemId].value * 2;
            if (CharLevelManager.manage.checkIfUnlocked(__instance.craftableItemId) && Inventory.Instance.allItems[itemId].craftable.workPlaceConditions != CraftingManager.CraftingMenuType.TrapperShop) { num = 0; }
            if (Inventory.Instance.wallet < num) { return false; }

            var recipe = ___currentVariation == -1 || Inventory.Instance.allItems[itemId].craftable.altRecipes.Length == 0 ? Inventory.Instance.allItems[itemId].craftable : Inventory.Instance.allItems[itemId].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.Instance.getInvItemId(recipe.itemsInRecipe[i]);
                int count = recipe.stackOfItemsInRecipe[i];
                if (GetItemCount(invItemId) < count) {
                    result = false;
                    break;
                }
            }

            __result = result;
            return false;
        }

        public void Update() { openChestWindow = !CraftMenuIsOpen; }

        public static bool takeItemsForRecipePatch(CraftingManager __instance, int currentlyCrafting, int ___currentVariation) {
            if (modDisabled) return true;
            var recipe = ___currentVariation == -1 || Inventory.Instance.allItems[currentlyCrafting].craftable.altRecipes.Length == 0 ? Inventory.Instance.allItems[currentlyCrafting].craftable : Inventory.Instance.allItems[currentlyCrafting].craftable.altRecipes[___currentVariation];

            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }

            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.Instance.getInvItemId(recipe.itemsInRecipe[i]);
                int amountToRemove = recipe.stackOfItemsInRecipe[i];
                var info = GetItem(invItemId);

                info.sources = info.sources.OrderBy(b => b.fuel).ThenBy(b => b.playerInventory == usePlayerInvFirst.Value ? 0 : 1).ToList();
                for (var d = 0; d < info.sources.Count; d++) {

                    // Cap out removed quantity at this source's quantity
                    var removed = Mathf.Min(amountToRemove, info.sources[d].quantity);

                    // If player inventory, remove from that
                    if (info.sources[d].playerInventory) { removeFromPlayerInventory(invItemId, info.sources[d].slotID, info.sources[d].quantity - removed); }

                    else {

                        // Remove from chest inventory on server
                        if (clientInServer) {
                            NetworkMapSharer.Instance.localChar.myPickUp.CmdChangeOneInChest(
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

        public static void removeFromPlayerInventory(int itemID, int slotID, int amountRemaining) {
            if (modDisabled) return;
            Inventory.Instance.invSlots[slotID].stack = amountRemaining;
            Inventory.Instance.invSlots[slotID].updateSlotContentsAndRefresh(amountRemaining == 0 ? -1 : itemID, amountRemaining);
        }

        [HarmonyPrefix] public static void openCloseCraftMenuPrefix(bool isMenuOpen) {
            CraftMenuIsOpen = isMenuOpen;
            if (isMenuOpen) { openingCraftMenu = true; }
        }

        [HarmonyPrefix]
        public static bool populateCraftListPrefix(CraftingManager __instance, CraftingManager.CraftingMenuType listType) {
            if (modDisabled || !openingCraftMenu) return true;
            openingCraftMenu = false;
            OnFinishedParsing = () => CraftingManager.manage.populateCraftList(listType);
            ParseAllItems();
            return false;
        }

        // We have to override this entirely because we need it to use our item counts, not the count of items in player inventory
        public static bool fillRecipeIngredientsPatch(CraftingManager __instance, int recipeNo, int variation) {
            if (modDisabled) return true;
            var recipe = variation == -1 || Inventory.Instance.allItems[recipeNo].craftable.altRecipes.Length == 0 ? Inventory.Instance.allItems[recipeNo].craftable : Inventory.Instance.allItems[recipeNo].craftable.altRecipes[variation];
            for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
                int invItemId = Inventory.Instance.getInvItemId(recipe.itemsInRecipe[i]);
                __instance.currentRecipeObjects.Add(Instantiate<GameObject>(__instance.recipeSlot, __instance.RecipeIngredients));
                __instance.currentRecipeObjects[__instance.currentRecipeObjects.Count - 1]
                          .GetComponent<FillRecipeSlot>()
                          .fillRecipeSlotWithAmounts(
                               invItemId, GetItemCount(invItemId), recipe.stackOfItemsInRecipe[i]
                           );
            }
            return false;
        }

        // Stops the chest popup window from opening when we're just checking what items are inside them
        public static bool openChestInWindowPrefix() {
            if (!openChestWindow || findingNearbyChests) { return false; }
            return true;
        }

        //public static bool playerOpenedChestPrefix() { return !findingNearbyChests; }

        [HarmonyPrefix]
        public static bool UserCode_RpcGiveOnTileStatusPrefix(ref int give, int xPos, int yPos) { return !(findingNearbyChests); }

        public static void FindNearbyChests() {
            numOfUnlockedChests = 0;
            var chests = new List<(ChestPlaceable chest, bool insideHouse)>();
            nearbyChests.Clear();

            // Gets the player's house
            // TODO: Make sure this works in multiplayer
            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }

            playerPosition = NetworkMapSharer.Instance.localChar.myInteract.transform.position;

            // public static bool InsideHouse => NetworkMapSharer.share.localChar.myInteract.insidePlayerHouse && clientInServer;

            /*
             * The current way we have chests set up to gather data uses the player's position
             * to check if the chest is inside a house or outside a house. This causes chests to be
             * deleted outside if the player is inside and vice versa. It is now only enabled
             * for clients in the area they are currently in and not everywhere. The host can
             * use the mod as they normally would though. 
             */

            // Gets chests inside houses iff not a client or the client is already inside the house
            if (ClientInsideHouse || !clientInServer) {
                //Plugin.LogToConsole($"Checking for chests inside the house");
                Collider[] chestsInsideHouse = Physics.OverlapBox(new Vector3(playerPosition.x, -88, playerPosition.z), new Vector3(tileRadius.Value * 2, 5, tileRadius.Value * 2), Quaternion.identity, LayerMask.GetMask(LayerMask.LayerToName(15)));
                for (var i = 0; i < chestsInsideHouse.Length; i++) {
                    ChestPlaceable chestComponent = chestsInsideHouse[i].GetComponentInParent<ChestPlaceable>();
                    if (chestComponent == null) continue;

                    //Plugin.LogToConsole("FOUND INSIDE HOUSE?: " + chestsInsideHouse[i].transform.position);
                    //Plugin.LogToConsole("COLLLIDER INFO: " + chestsInsideHouse[i].GetComponentInChildren<Collider>().bounds);
                    chests.Add((chestComponent, true));
                }
            }

            // Gets chests in the overworld iff not a client or client is outside the house
            if (ClientOutsideHouse || !clientInServer) {
                //Plugin.LogToConsole($"Checking for chests outside the house");
                Collider[] chestsOutside = Physics.OverlapBox(new Vector3(playerPosition.x, -7, playerPosition.z), new Vector3(tileRadius.Value * 2, 20, tileRadius.Value * 2), Quaternion.identity, LayerMask.GetMask(LayerMask.LayerToName(15)));
                for (var j = 0; j < chestsOutside.Length; j++) {
                    ChestPlaceable chestComponent = chestsOutside[j].GetComponentInParent<ChestPlaceable>();
                    if (chestComponent == null) continue;

                    //Plugin.LogToConsole("FOUND OUTSIDE HOUSE?: " + chestsOutside[j].transform.position);
                    //Plugin.LogToConsole("COLLLIDER INFO: " + chestsOutside[j].GetComponentInChildren<Collider>().bounds);
                    chests.Add((chestComponent, false));
                }
            }

            for (var k = 0; k < chests.Count; k++) {

                // Gets the chest's tile position
                var tempX = chests[k].chest.myXPos();
                var tempY = chests[k].chest.myYPos();

                Plugin.Log($"{k} - Position: ({chests[k].chest.myXPos()}, {chests[k].chest.myYPos()})");

                // TODO: Currently, this gets the player's house if its inside a house at all
                // TODO: Make this get the correct house --- I think playerHouse gets the current players house and not the hosts, it works for me, but you didnt see them...
                HouseDetails house = chests[k].insideHouse ? playerHouse : null;

                // If we're a client on a server, this tells the server to open the chest and adds the chest to a list of chests that need
                // to be checked again once we have up to date information from the server
                if (clientInServer) {
                    //if (unconfirmedChests.ContainsKey((tempX, tempY))) { Plugin.LogToConsole("CHEST AT " + tempX + ", " + tempY + " already in dictionary"); }
                    unconfirmedChests[(tempX, tempY)] = house;
                    NetworkMapSharer.Instance.localChar.myPickUp.CmdOpenChest(tempX, tempY);
                    NetworkMapSharer.Instance.localChar.CmdCloseChest(tempX, tempY);
                }

                // If we're a host or in single player, then we just need to check the chest if its empty cause that auto-creates chests when necessary
                else {
                    ContainerManager.manage.checkIfEmpty(tempX, tempY, house);
                    AddChest(tempX, tempY, house);
                }

            }

        }

        private static void AddChest(int xPos, int yPos, HouseDetails house) {
            nearbyChests.Add((ContainerManager.manage.activeChests.First(i => i.xPos == xPos && i.yPos == yPos), house));
            nearbyChests = nearbyChests.Distinct().ToList();
        }

        [HarmonyPostfix]
        public static void UserCode_TargetOpenChestPostfix(ContainerManager __instance, int xPos, int yPos, int[] itemIds, int[] itemStack) {
            // TODO: Get proper house details
            if (unconfirmedChests.TryGetValue((xPos, yPos), out var house)) {
                numOfUnlockedChests += 1;
                unconfirmedChests.Remove((xPos, yPos));
                AddChest(xPos, yPos, house);
            }
        }

        [HarmonyPrefix]
        public static void TargetOpenChestPrefix(NetworkConnection con, int xPos, int yPos, int[] itemIds, int[] itemStack) {
            Plugin.Log($"TargetOpenChestPrefix: Connection - {con} | ({xPos}, {yPos})");
            Plugin.Log($"TargetOpenChestPrefix: Connection - {NetworkClient.connection} | ({xPos}, {yPos})");

        }

        // Fills a dictionary with info about the items in player inventory and nearby chests
        public static void ParseAllItems() {
            if (!modDisabled) {
                instance.StopAllCoroutines();
                instance.StartCoroutine(ParseAllItemsRoutine());
            }
        }

        public static IEnumerator ParseAllItemsRoutine() {

            // Recreate known chests and clear items
            findingNearbyChests = true;
            FindNearbyChests();
            if (clientInServer) {
                yield return new WaitForSeconds(.5f);
                Plugin.Log($"Probably Locked - CountRemaining: {unconfirmedChests.Count}");
            } //WaitUntil(() => unconfirmedChests.Count <= 0); }
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.Instance.invSlots.Length; i++) {
                if (!TRItems.GetItemDetails(Inventory.Instance.invSlots[i].itemNo))continue;
                AddItem(Inventory.Instance.invSlots[i].itemNo, Inventory.Instance.invSlots[i].stack, i, TRItems.GetItemDetails(Inventory.Instance.invSlots[i].itemNo).checkIfStackable(), null, null);
            }

            // Get all items in nearby chests
            //Plugin.LogToConsole($"Size of ChestInfo: {nearbyChests.Count}");
            foreach (var ChestInfo in nearbyChests) {
                for (var i = 0; i < ChestInfo.chest.itemIds.Length; i++) {
                    if (!TRItems.GetItemDetails(ChestInfo.chest.itemIds[i])) continue;
                    AddItem(ChestInfo.chest.itemIds[i], ChestInfo.chest.itemStacks[i], i, TRItems.GetItemDetails(ChestInfo.chest.itemIds[i]).checkIfStackable(), ChestInfo.house, ChestInfo.chest);
                }
            }

            findingNearbyChests = false;
            OnFinishedParsing?.Invoke();
            OnFinishedParsing = null;

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
