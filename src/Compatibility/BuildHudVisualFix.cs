using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CraftingStationNetwork.Compatibility
{
    internal static class BuildHudVisualFix
    {
        internal static int Patch(Harmony harmony)
        {
            if (harmony == null)
            {
                return 0;
            }

            int patched = 0;
            MethodInfo[] methods = typeof(Hud).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != "SetupPieceInfo")
                {
                    continue;
                }

                var postfix = new HarmonyMethod(typeof(BuildHudVisualFix), nameof(SetupPieceInfoPostfix))
                {
                    priority = Priority.Last
                };

                harmony.Patch(method, postfix: postfix);
                patched++;
            }

            return patched;
        }

        private static void SetupPieceInfoPostfix(Hud __instance, object[] __args)
        {
            if (!CraftingStorageLinkCompat.IsActive || __instance == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            Piece piece = FindPiece(__args);

            if (player == null || piece == null || piece.m_resources == null || __instance.m_requirementItems == null)
            {
                return;
            }

            int rowCount = Math.Min(piece.m_resources.Length, __instance.m_requirementItems.Length);

            for (int i = 0; i < rowCount; i++)
            {
                Piece.Requirement requirement = piece.m_resources[i];
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    continue;
                }

                GameObject row = __instance.m_requirementItems[i];
                if (row == null)
                {
                    continue;
                }

                Transform amountTransform = row.transform.Find("res_amount");
                TMP_Text amountText = amountTransform == null ? null : amountTransform.GetComponent<TMP_Text>();
                if (amountText == null || !string.IsNullOrWhiteSpace(amountText.text))
                {
                    continue;
                }

                int available = CraftingStorageLinkCompat.CountAvailableForBuildDisplay(player, piece, requirement);
                amountText.text = $"{available}/{requirement.m_amount}";

                if (Plugin.Settings?.VerboseDiagnostics.Value == true)
                {
                    string itemName = requirement.m_resItem.m_itemData?.m_shared?.m_name ?? "<unknown-item>";
                    Plugin.DebugLogOnce(
                        $"hud-blank-fix:{piece.GetInstanceID()}:{i}:{available}:{requirement.m_amount}",
                        $"HUD visual fallback restored blank requirement count for piece='{piece.m_name}', item='{itemName}', display={available}/{requirement.m_amount}.");
                }
            }
        }

        private static Piece FindPiece(object[] args)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Piece piece)
                {
                    return piece;
                }
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return null;
            }

            try
            {
                PieceTable table = player.GetBuildTool();
                return table == null ? null : table.GetSelectedPiece();
            }
            catch
            {
                return null;
            }
        }
    }
}
