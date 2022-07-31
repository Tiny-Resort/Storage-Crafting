using HarmonyLib;
using UnityEngine;

namespace TR {
    
    public class Tools : MonoBehaviour {

        public static void Initialize(Harmony harmony) { harmony.PatchAll(); }

        #region Easy Notifications
        
        public static bool forceClearNotification;

        /// <summary>
        /// Displays a notification at the top of the screen right away rather than waiting on any previous notifications.
        /// </summary>
        /// <param name="title">Large text at the top of the notification.</param>
        /// <param name="subtitle">Smaller descriptive text below the title.</param>
        public static void Notify(string title, string subtitle) {
            forceClearNotification = true;
            NotificationManager.manage.makeTopNotification(title, subtitle);
        }
        
        #endregion
        
        #region Version Checking

        public static string currentGameVersion;

        public static string CheckGameVersion(string myModGameVersion) {
                
            if (!WorldManager.manageWorld) { return "Game not fully loaded. Can not compare version numbers."; }
            currentGameVersion = "v0." + WorldManager.manageWorld.masterVersionNumber.ToString() + "." + WorldManager.manageWorld.versionNumber.ToString();

            return myModGameVersion == currentGameVersion ? 
                       "Mod was created using this version of the game." : 
                       "Mod was created for a different version of the game than is running. Issues may occur.";
        }
        
        #endregion
        
        #region User Interface

        public static void DrawWindow() {
            
        }
        
        #endregion

    }
}
