using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using UnityEngine.InputSystem;

namespace TR {

    [HarmonyPatch(typeof(NotificationManager), "makeTopNotification")]
    public class NotificationPatch {
        
        // Forcibly clears the top notification so that it can be replaced immediately
        [HarmonyPrefix]
        public static bool makeTopNotificationPrefix(NotificationManager __instance) {

            if (Tools.forceClearNotification) {
                Tools.forceClearNotification = false;

                var toNotify = (List<string>)AccessTools.Field(typeof(NotificationManager), "toNotify").GetValue(__instance);
                var subTextNot = (List<string>)AccessTools.Field(typeof(NotificationManager), "subTextNot").GetValue(__instance);
                var soundToPlay = (List<ASound>)AccessTools.Field(typeof(NotificationManager), "soundToPlay").GetValue(__instance);
                var topNotificationRunning = AccessTools.Field(typeof(NotificationManager), "topNotificationRunning");
                var topNotificationRunningRoutine = topNotificationRunning.GetValue(__instance);

                // Clears existing notifications in the queue
                toNotify.Clear();
                subTextNot.Clear();
                soundToPlay.Clear();

                // Stops the current coroutine from continuing
                if (topNotificationRunningRoutine != null) {
                    __instance.StopCoroutine((Coroutine)topNotificationRunningRoutine);
                    topNotificationRunning.SetValue(__instance, null);
                }

                // Resets all animations related to the notificatin bubble appearing/disappearing
                __instance.StopCoroutine("closeWithMask");
                __instance.topNotification.StopAllCoroutines();
                var Anim = __instance.topNotification.GetComponent<WindowAnimator>();
                Anim.StopAllCoroutines();
                Anim.maskChild.enabled = false;
                Anim.contents.gameObject.SetActive(false);
                Anim.gameObject.SetActive(false);

                return true;

            }
            else return true;
        }
        
    }
    
}