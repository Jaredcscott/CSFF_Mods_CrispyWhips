using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Forces the on-screen durability/stat display to redraw after a reflection-based
    /// stat write (<see cref="CSFFModFramework.Util.CardUtil.SetDurability"/> writes the
    /// backing field directly and does not notify <c>CardVisuals</c> — see root CLAUDE.md
    /// "Runtime Card State Caching"). Shared by InnPatch and AcademyPatch, whose account
    /// balances are SpecialDurability stats displayed via TextDisplay:2.
    /// Mirrors WaterDrivenInfrastructure's ActionInterceptPatch.RefreshCardDurabilityVisuals
    /// / RefreshOpenInventoryPopup.
    /// </summary>
    internal static class CardVisualsRefresh
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _durabilityRefreshFailureLogged;
        private static bool _inventoryPopupRefreshFailureLogged;

        // Keyed by the card's concrete runtime type: callers pass ActionContext.Card, which is
        // typed `object` at the framework boundary, so a single shared static FieldInfo/MethodInfo
        // would risk the CLAUDE.md B5 cache-corruption hazard if a caller's concrete type differs.
        private static readonly Dictionary<Type, (MemberInfo VisualsMember, MethodInfo RefreshMethod)> _durabilityCache = new();

        /// <summary>Re-renders the card's own durability bar/number (world-space stat display).</summary>
        public static void RefreshDurabilityVisuals(object card)
        {
            try
            {
                if (card == null) return;
                var cardType = card.GetType();
                if (!_durabilityCache.TryGetValue(cardType, out var cached))
                {
                    MemberInfo visualsMember = cardType.GetProperty("CardVisuals", Flags)
                        ?? (MemberInfo)cardType.GetField("CardVisuals", Flags);
                    Type visualsType = visualsMember switch
                    {
                        PropertyInfo pi => pi.PropertyType,
                        FieldInfo fi => fi.FieldType,
                        _ => null
                    };
                    cached = (visualsMember, visualsType?.GetMethod("RefreshDurabilities", Flags));
                    _durabilityCache[cardType] = cached;
                }

                var visuals = cached.VisualsMember switch
                {
                    PropertyInfo pi => pi.GetValue(card),
                    FieldInfo fi => fi.GetValue(card),
                    _ => null
                };
                cached.RefreshMethod?.Invoke(visuals, null);
            }
            catch (Exception ex)
            {
                if (!_durabilityRefreshFailureLogged)
                {
                    _durabilityRefreshFailureLogged = true;
                    Plugin.Logger.LogDebug($"[CardVisualsRefresh] RefreshDurabilityVisuals reflection failed (durability display may not refresh): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }

        // GraphicsManager/CurrentInspectionPopup are fixed game types — resolved once, not per call.
        private static Type _graphicsManagerType;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _currentPopupField;
        private static MethodInfo _refreshInventoryMethod;
        private static bool _staticsResolved;

        /// <summary>Re-renders an already-open inspection popup, if the changed card is shown in one.</summary>
        public static void RefreshOpenInventoryPopup()
        {
            try
            {
                if (!_staticsResolved)
                {
                    _staticsResolved = true;
                    _graphicsManagerType = AccessTools.TypeByName("GraphicsManager");
                    _instanceProperty = _graphicsManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _currentPopupField = _graphicsManagerType != null ? AccessTools.Field(_graphicsManagerType, "CurrentInspectionPopup") : null;
                    _refreshInventoryMethod = _currentPopupField?.FieldType.GetMethod("RefreshInventory", Flags);
                }
                if (_graphicsManagerType == null) return;

                var instance = _instanceProperty?.GetValue(null)
                    ?? UnityEngine.Object.FindObjectOfType(_graphicsManagerType);
                var popup = instance == null ? null : _currentPopupField?.GetValue(instance);
                if (popup != null)
                    _refreshInventoryMethod?.Invoke(popup, null);
            }
            catch (Exception ex)
            {
                if (!_inventoryPopupRefreshFailureLogged)
                {
                    _inventoryPopupRefreshFailureLogged = true;
                    Plugin.Logger.LogDebug($"[CardVisualsRefresh] RefreshOpenInventoryPopup reflection failed (open popup may not refresh): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }
    }
}
