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

        internal static CraftFromStorage instance;

        public static TRPlugin Plugin;
        public const string pluginGuid = "dev.TinyResort.CraftFromStorage";
        public const string pluginName = "Craft From Storage";
        public const string pluginVersion = "0.8.2";

        internal delegate void ParsingEvent();
        internal static ParsingEvent OnFinishedParsing;

        internal static ConfigEntry<int> tileRadius;
        internal static ConfigEntry<bool> usePlayerInvFirst;

        internal static Vector3 playerPosition;

        internal static List<(Chest chest, HouseDetails house)> nearbyChests = new List<(Chest chest, HouseDetails house)>();
        internal static Dictionary<(int xPos, int yPos), HouseDetails> unconfirmedChests = new Dictionary<(int xPos, int yPos), HouseDetails>();
        internal static Dictionary<int, ItemInfo> nearbyItems = new Dictionary<int, ItemInfo>();

        internal static HouseDetails playerHouse;

        internal static bool clientInServer;
        internal static bool openingCraftMenu;
        internal static bool CraftMenuIsOpen;
        internal static bool tryingCraftItem;
        internal static bool runCraftItemPostfix;
        internal static bool findingNearbyChests;
        internal static bool openChestWindow;

        internal static bool ClientInsideHouse => NetworkMapSharer.share.localChar.myInteract.insidePlayerHouse && clientInServer;
        internal static bool ClientOutsideHouse => !NetworkMapSharer.share.localChar.myInteract.insidePlayerHouse && clientInServer;

        internal static bool modDisabled => RealWorldTimeLight.time.underGround;

        internal static int numOfUnlockedChests;

        private void Awake() {

            Plugin = TRTools.Initialize(this, 28);
            instance = this;

            #region Configuration

            tileRadius = Config.Bind<int>("General", "Range", 30, "Increases the range it looks for storage containers by an approximate tile count.");
            usePlayerInvFirst = Config.Bind<bool>("General", "UsePlayerInventoryFirst", true, "Sets whether it pulls items out of player's inventory first (pulls from chests first if false)");
            #endregion 
            
            Plugin.harmony.PatchAll();

            Plugin.AddConflictingPlugin("tinyresort.dinkum.craftFromStorage");
            
        }

        internal void Update() { openChestWindow = !CraftMenuIsOpen; }

        internal static void removeFromPlayerInventory(int itemID, int slotID, int amountRemaining) {
            if (modDisabled) return;
            Inventory.inv.invSlots[slotID].stack = amountRemaining;
            Inventory.inv.invSlots[slotID].updateSlotContentsAndRefresh(amountRemaining == 0 ? -1 : itemID, amountRemaining);
        }
        
        public static void FindNearbyChests() {
            numOfUnlockedChests = 0;
            var chests = new List<(ChestPlaceable chest, bool insideHouse)>();
            nearbyChests.Clear();

            // Gets the player's house
            // TODO: Make sure this works in multiplayer
            for (int i = 0; i < HouseManager.manage.allHouses.Count; i++) {
                if (HouseManager.manage.allHouses[i].isThePlayersHouse) { playerHouse = HouseManager.manage.allHouses[i]; }
            }

            playerPosition = NetworkMapSharer.share.localChar.myInteract.transform.position;

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
                    NetworkMapSharer.share.localChar.myPickUp.CmdOpenChest(tempX, tempY);
                    NetworkMapSharer.share.localChar.CmdCloseChest(tempX, tempY);
                }

                // If we're a host or in single player, then we just need to check the chest if its empty cause that auto-creates chests when necessary
                else {
                    ContainerManager.manage.checkIfEmpty(tempX, tempY, house);
                    AddChest(tempX, tempY, house);
                }

            }

        }

        // Fills a dictionary with info about the items in player inventory and nearby chests
        internal static void ParseAllItems() {
            if (!modDisabled) {
                instance.StopAllCoroutines();
                instance.StartCoroutine(ParseAllItemsRoutine());
            }
        }

        internal static IEnumerator ParseAllItemsRoutine() {

            // Recreate known chests and clear items
            findingNearbyChests = true;
            FindNearbyChests();
            if (clientInServer) {
                yield return new WaitForSeconds(.5f);
                Plugin.Log($"Probably Locked - CountRemaining: {unconfirmedChests.Count}");
            } //WaitUntil(() => unconfirmedChests.Count <= 0); }
            nearbyItems.Clear();

            // Get all items in player inventory
            for (var i = 0; i < Inventory.inv.invSlots.Length; i++) {
                if (Inventory.inv.invSlots[i].itemNo == -1) continue;
                if (!TRItems.GetItemDetails(Inventory.inv.invSlots[i].itemNo)) continue;
                AddItem(Inventory.inv.invSlots[i].itemNo, Inventory.inv.invSlots[i].stack, i, TRItems.GetItemDetails(Inventory.inv.invSlots[i].itemNo).checkIfStackable(), null, null);
            }

            // Get all items in nearby chests
            //Plugin.LogToConsole($"Size of ChestInfo: {nearbyChests.Count}");
            foreach (var ChestInfo in nearbyChests) {
                for (var i = 0; i < ChestInfo.chest.itemIds.Length; i++) {
                    if (ChestInfo.chest.itemIds[i] == -1) continue;
                    if (!TRItems.GetItemDetails(ChestInfo.chest.itemIds[i])) continue;
                    AddItem(ChestInfo.chest.itemIds[i], ChestInfo.chest.itemStacks[i], i, TRItems.GetItemDetails(ChestInfo.chest.itemIds[i]).checkIfStackable(), ChestInfo.house, ChestInfo.chest);
                }
            }

            findingNearbyChests = false;
            OnFinishedParsing?.Invoke();
            OnFinishedParsing = null;

        }
        
        internal static int GetItemCount(int itemID) => nearbyItems.TryGetValue(itemID, out var info) ? info.quantity : 0;

        internal static ItemInfo GetItem(int itemID) => nearbyItems.TryGetValue(itemID, out var info) ? info : null;
        
        internal static void AddItem(int itemID, int quantity, int slotID, bool isStackable, HouseDetails isInHouse, Chest chest) {

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

        internal static void AddChest(int xPos, int yPos, HouseDetails house) {
            nearbyChests.Add((ContainerManager.manage.activeChests.First(i => i.xPos == xPos && i.yPos == yPos), house));
            nearbyChests = nearbyChests.Distinct().ToList();
        }

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
