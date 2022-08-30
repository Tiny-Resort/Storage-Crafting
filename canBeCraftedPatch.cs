using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TinyResort {

    [HarmonyPatch]
    public class canBeCraftedPatch {

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.canBeCrafted))]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            return new CodeMatcher(instructions)
                  .MatchForward(
                       false, 
                       new CodeMatch(OpCodes.Ldsfld),
                       new CodeMatch(OpCodes.Ldloc_3),
                       new CodeMatch(i => i.opcode == OpCodes.Callvirt && i.operand.ToString().Contains("getAmountOfItemInAllSlots"))
                   )
                  .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldsfld, AccessTools.Method(typeof(CraftFromStorage), "GetItemCount")))
                  .InsertAndAdvance(
                       new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromStorage), "GetItemCount"))
                   ).InstructionEnumeration();
        }

    }

}

/*var codes = new List<CodeInstruction>(instructions);
foreach (CodeInstruction instruction in instructions) {
    if (instruction.opcode == OpCodes.Callvirt) {
        var strOperand = instruction.operand.ToString();
        if (strOperand == "Int32 getAmountOfItemInAllSlots(Int32)") {
            yield return Transpilers.EmitDelegate<Func<int, int>>(foo => foo + 5);
        }
    }
}*/
            //return codes.AsEnumerable();


/*for (int i = 0; i < recipe.itemsInRecipe.Length; i++) {
    int invItemId = Inventory.inv.getInvItemId(recipe.itemsInRecipe[i]);
    int count = recipe.stackOfItemsInRecipe[i];
    if (GetItemCount(invItemId) < count) {
        result = false;
        break;
    }
}*/

