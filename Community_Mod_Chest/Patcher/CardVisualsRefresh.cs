using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Forces the on-screen durability/stat display to redraw after a reflection-based
    /// stat write (<see cref="CSFFModFramework.Util.CardUtil.SetDurability"/> writes the
    /// backing field directly and does not notify <c>CardVisuals</c>: see root CLAUDE.md
    /// "Runtime Card State Caching"). Shared by InnPatch and AcademyPatch, whose account
    /// balances are SpecialDurability stats displayed via TextDisplay:2.
    /// Mirrors WaterDrivenInfrastructure's ActionInterceptPatch.RefreshCardDurabilityVisuals
    /// / RefreshOpenInventoryPopup.
    ///
    /// <para>Every game member this reaches by name (<c>InGameCardBase.CardVisuals</c>,
    /// <c>CardGraphics.RefreshDurabilities</c>, <c>GraphicsManager.CurrentInspectionPopup</c>,
    /// <c>InspectionPopup.RefreshInventory</c>) is resolved once and, when it does not resolve, named
    /// in one warning (modwide A13, 2026-11-12). Before that a renamed member made the refresh a
    /// silent no-op: no exception is thrown on a null resolve, so the catch never ran. A missing
    /// refresh is cosmetic (the value is right and shows on reopen), so it warns and carries on.</para>
    /// </summary>
    internal static class CardVisualsRefresh
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // One latch per cause (root CLAUDE.md Silent Catch Blocks).
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[CardVisualsRefresh] " + message + " Logged once.");
        }

        // Keyed by the card's concrete runtime type: callers pass ActionContext.Card, which is
        // typed `object` at the framework boundary, so a single shared static FieldInfo/MethodInfo
        // would risk the CLAUDE.md B5 cache-corruption hazard if a caller's concrete type differs.
        private static readonly Dictionary<Type, (MemberInfo VisualsMember, MethodInfo RefreshMethod)> _durabilityCache = new();

        /// <summary>Property first, then field: a field that a game update turns into an auto-property
        /// (or the reverse) still resolves.</summary>
        private static MemberInfo PropertyOrField(Type type, string name) =>
            type == null ? null : (MemberInfo)type.GetProperty(name, Flags) ?? type.GetField(name, Flags);

        private static Type MemberType(MemberInfo member) => member switch
        {
            PropertyInfo pi => pi.PropertyType,
            FieldInfo fi => fi.FieldType,
            _ => null
        };

        private static object MemberValue(MemberInfo member, object instance) => member switch
        {
            PropertyInfo pi => pi.GetValue(instance),
            FieldInfo fi => fi.GetValue(instance),
            _ => null
        };

        /// <summary>Re-renders the card's own durability bar/number (world-space stat display).</summary>
        public static void RefreshDurabilityVisuals(object card)
        {
            try
            {
                if (card == null) return;
                var cardType = card.GetType();
                if (!_durabilityCache.TryGetValue(cardType, out var cached))
                {
                    MemberInfo visualsMember = PropertyOrField(cardType, "CardVisuals");
                    if (visualsMember == null)
                        WarnOnce("cardvisuals-missing:" + cardType.Name, cardType.Name + ".CardVisuals did not resolve (renamed by a game update?); a card's own stat display does not redraw after a balance write until it is reopened.");
                    Type visualsType = MemberType(visualsMember);
                    MethodInfo refreshMethod = visualsType?.GetMethod("RefreshDurabilities", Flags);
                    if (visualsType != null && refreshMethod == null)
                        WarnOnce("refreshdurabilities-missing:" + visualsType.Name, visualsType.Name + ".RefreshDurabilities did not resolve (renamed by a game update?); a card's own stat display does not redraw after a balance write until it is reopened.");
                    cached = (visualsMember, refreshMethod);
                    _durabilityCache[cardType] = cached;
                }

                if (cached.VisualsMember == null || cached.RefreshMethod == null) return; // said once above
                var visuals = MemberValue(cached.VisualsMember, card);
                if (visuals == null) return; // a card with no visuals yet (not on screen): nothing to redraw
                cached.RefreshMethod.Invoke(visuals, null);
            }
            catch (Exception ex)
            {
                WarnOnce("durability-refresh-exception:" + (ex.InnerException ?? ex).GetType().Name,
                    $"RefreshDurabilityVisuals reflection failed (durability display may not refresh): {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // GraphicsManager/CurrentInspectionPopup are fixed game types: resolved once, not per call.
        private static Type _graphicsManagerType;
        private static PropertyInfo _instanceProperty;
        private static MemberInfo _currentPopupMember;
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
                    if (_graphicsManagerType == null)
                    {
                        WarnOnce("graphicsmanager-type-missing", "GraphicsManager type not found; an open inspection popup does not redraw after a balance write until it is reopened.");
                    }
                    else
                    {
                        // Optional: FindObjectOfType below covers a missing static Instance.
                        _instanceProperty = _graphicsManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        // Property first, then field (modwide A2): CurrentInspectionPopup is a plain field today,
                        // and AccessTools.Field alone returned null, silently, the day it becomes an auto-property.
                        _currentPopupMember = PropertyOrField(_graphicsManagerType, "CurrentInspectionPopup");
                        if (_currentPopupMember == null)
                            WarnOnce("currentinspectionpopup-missing", "GraphicsManager.CurrentInspectionPopup did not resolve (renamed by a game update?); an open inspection popup does not redraw after a balance write until it is reopened.");
                        Type popupType = MemberType(_currentPopupMember);
                        _refreshInventoryMethod = popupType?.GetMethod("RefreshInventory", Flags);
                        if (popupType != null && _refreshInventoryMethod == null)
                            WarnOnce("refreshinventory-missing", popupType.Name + ".RefreshInventory did not resolve (renamed by a game update?); an open inspection popup does not redraw after a balance write until it is reopened.");
                    }
                }
                if (_graphicsManagerType == null || _currentPopupMember == null || _refreshInventoryMethod == null) return; // said once above

                var instance = _instanceProperty?.GetValue(null)
                    ?? UnityEngine.Object.FindObjectOfType(_graphicsManagerType);
                var popup = instance == null ? null : MemberValue(_currentPopupMember, instance);
                if (popup != null)
                    _refreshInventoryMethod.Invoke(popup, null);
            }
            catch (Exception ex)
            {
                WarnOnce("popup-refresh-exception:" + (ex.InnerException ?? ex).GetType().Name,
                    $"RefreshOpenInventoryPopup reflection failed (open popup may not refresh): {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
