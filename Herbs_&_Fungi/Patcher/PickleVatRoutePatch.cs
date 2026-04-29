using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Two responsibilities:
    /// 1) On Make Brine: swap the contained liquid (water → pickle brine) using the in-place
    ///    CardModel pattern. Vat itself stays as PicklingVat.
    /// 2) Pickle BP gate: if a player picks a pickle blueprint while the vat's contained liquid
    ///    is anything other than pickle_brine, block execution. Quantity gate alone (≥500)
    ///    can't tell water from brine, so we enforce liquid type here in code.
    /// </summary>
    public static class PickleVatRoutePatch
    {
        private const string PicklingVatID = "herbs_fungi_pickling_vat";
        private const string BrineID       = "herbs_fungi_pickle_brine";
        private const string MakeBrineKey  = "Herbs_And_Fungi_PicklingVat_MakeBrine_ActionName";

        private static readonly HashSet<string> PickleBpUids = new HashSet<string>
        {
            "herbs_fungi_bp_pickle_frogs",
            "herbs_fungi_bp_pickle_mushrooms",
            "herbs_fungi_bp_pickle_meat",
            "herbs_fungi_bp_pickle_vegetables",
        };

        private static readonly HashSet<string> PickleBpNameKeys = new HashSet<string>
        {
            "Herbs_And_Fungi_Bp_PickleFrogs_CardName",
            "Herbs_And_Fungi_Bp_PickleMushrooms_CardName",
            "Herbs_And_Fungi_Bp_PickleMeat_CardName",
            "Herbs_And_Fungi_Bp_PickleVegetables_CardName",
        };

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type _cardDataType;
        private static Type _uidType;
        private static MethodInfo _getFromID;
        private static bool _reflInit;

        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmt = AccessTools.TypeByName("GameManager");
                if (gmt == null) { Logger?.LogError("[PickleVat] GameManager not found"); return; }

                HookByName(harmony, gmt, "ActionRoutine",
                    nameof(ActionRoutine_Prefix));
                HookByName(harmony, gmt, "CardOnCardActionRoutine",
                    nameof(CardOnCardActionRoutine_Prefix));
                HookByName(harmony, gmt, "PerformStackActionRoutine",
                    nameof(PerformStackActionRoutine_Prefix));
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] ApplyPatch: {ex.Message}"); }
        }

        private static void HookByName(Harmony harmony, Type t, string methodName, string prefixName)
        {
            var method = t.GetMethods(All)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length >= 2);
            if (method == null) { Logger?.LogError($"[PickleVat] {methodName} not found"); return; }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PickleVatRoutePatch), prefixName));
        }

        // ActionRoutine(CardAction action, InGameCardBase receiving, ...) — most card actions, including BP execution from a contained-blueprint click.
        private static bool ActionRoutine_Prefix(object __0, object __1)
        {
            return Gate(action: __0, receiver: __1);
        }

        // CardOnCardActionRoutine(action, given, receiving, ...) — drag-drop CIs.
        private static bool CardOnCardActionRoutine_Prefix(object __0, object __1, object __2)
        {
            return Gate(action: __0, receiver: __2);
        }

        // PerformStackActionRoutine(stackAction, receiving, ...) — stack-based actions.
        private static bool PerformStackActionRoutine_Prefix(object __0, object __1)
        {
            return Gate(action: __0, receiver: __1);
        }

        /// <summary>
        /// Shared dispatch: handles MakeBrine swap, then pickle-BP brine-required gate.
        /// Returns false to block the original (only when the BP gate trips).
        /// </summary>
        private static bool Gate(object action, object receiver)
        {
            try
            {
                if (action == null) return true;

                // Make Brine: liquid swap (water → brine), allow original to proceed
                if (receiver != null && IsMakeBrine(action) && GetUID(receiver) == PicklingVatID)
                {
                    SwapLiquidToBrine(receiver);
                    return true;
                }

                // Pickle BP gate: only allow when contained liquid is brine
                if (IsPickleBp(action))
                {
                    var liquidUid = GetContainedLiquidUid(receiver);
                    if (liquidUid == BrineID) return true;
                    Logger?.LogInfo($"[PickleBpGate] Blocked: liquid='{liquidUid ?? "none"}' on receiver='{GetUID(receiver) ?? "?"}' — pickle recipes require brine.");
                    return false;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] Gate: {ex.InnerException?.Message ?? ex.Message}"); }
            return true;
        }

        private static bool IsPickleBp(object actionOrCard)
        {
            if (actionOrCard == null) return false;
            // Direct UniqueID (BlueprintCard CardData when passed as the action)
            var uid = GetUID(actionOrCard);
            if (uid != null && PickleBpUids.Contains(uid)) return true;
            // ActionName key (CardAction wrapper)
            var name = Member(actionOrCard, "ActionName");
            var key = Member(name, "LocalizationKey") as string;
            if (key != null && PickleBpNameKeys.Contains(key)) return true;
            // CardName key (BlueprintCard CardData fallback)
            var cardName = Member(actionOrCard, "CardName");
            key = Member(cardName, "LocalizationKey") as string;
            if (key != null && PickleBpNameKeys.Contains(key)) return true;
            return false;
        }

        private static string GetContainedLiquidUid(object vat)
        {
            if (vat == null) return null;
            var liquidField = FindField(vat.GetType(), "ContainedLiquid");
            var liquidCard = liquidField?.GetValue(vat);
            return GetUID(liquidCard);
        }

        private static void SwapLiquidToBrine(object vat)
        {
            InitReflection();
            if (_getFromID == null) { Logger?.LogError("[MakeBrine] reflection not ready"); return; }

            var brineData = _getFromID.Invoke(null, new object[] { BrineID });
            if (brineData == null) { Logger?.LogError($"[MakeBrine] CardData not found: {BrineID}"); return; }

            var liquidField = FindField(vat.GetType(), "ContainedLiquid");
            if (liquidField == null) { Logger?.LogError("[MakeBrine] ContainedLiquid field not found on vat"); return; }

            var liquidCard = liquidField.GetValue(vat);
            if (liquidCard == null) { Logger?.LogError("[MakeBrine] vat has no ContainedLiquid to swap"); return; }

            var setModel = liquidCard.GetType().GetMethod("SetModel", All, null, new[] { brineData.GetType() }, null);
            if (setModel != null)
            {
                try { setModel.Invoke(liquidCard, new[] { brineData }); }
                catch (Exception ex) { Logger?.LogError($"[MakeBrine] SetModel threw: {ex.InnerException?.Message ?? ex.Message}"); return; }
            }
            else
            {
                if (!TrySetCardModel(liquidCard, brineData))
                {
                    Logger?.LogError($"[MakeBrine] CardModel not settable on {liquidCard.GetType().Name}");
                    return;
                }
                ReinitCard(liquidCard, brineData);
            }

            var future = FindField(vat.GetType(), "FutureLiquidContained");
            future?.SetValue(vat, brineData);

            RefreshCardVisuals(liquidCard);
            RefreshCardVisuals(vat);
            Logger?.LogInfo("[MakeBrine] Liquid swapped to brine");
        }

        private static void RefreshCardVisuals(object card)
        {
            try
            {
                var visualsField = FindField(card.GetType(), "CardVisuals");
                var visuals = visualsField?.GetValue(card);
                if (visuals == null) return;

                var setup = visuals.GetType().GetMethods(All)
                    .FirstOrDefault(m => m.Name == "Setup" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsInstanceOfType(card));
                setup?.Invoke(visuals, new[] { card });
            }
            catch (Exception ex) { Logger?.LogError($"[MakeBrine] RefreshCardVisuals: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        private static bool TrySetCardModel(object card, object cardData)
        {
            var t = card.GetType();
            try
            {
                var prop = FindProperty(t, "CardModel");
                if (prop != null)
                {
                    if (prop.CanWrite) { prop.SetValue(card, cardData, null); return true; }
                    var setter = prop.GetSetMethod(true);
                    if (setter != null) { setter.Invoke(card, new[] { cardData }); return true; }
                }
            }
            catch (Exception ex) { Logger?.LogError($"[MakeBrine] CardModel setter failed: {ex.InnerException?.Message ?? ex.Message}"); }

            var backing = FindField(t, "<CardModel>k__BackingField");
            if (backing != null)
            {
                backing.SetValue(card, cardData);
                return true;
            }

            return false;
        }

        private static void ReinitCard(object card, object cardData)
        {
            var t = card.GetType();
            var setup = t.GetMethods(All).FirstOrDefault(m => m.Name == "SetupCardSource"
                && m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType.IsInstanceOfType(cardData));
            if (setup != null)
            {
                var parms = setup.GetParameters();
                var args = new object[parms.Length];
                args[0] = cardData;
                for (int i = 1; i < parms.Length; i++)
                {
                    var pt = parms[i].ParameterType;
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                try { setup.Invoke(card, args); return; }
                catch (Exception ex) { Logger?.LogError($"[MakeBrine] SetupCardSource failed: {ex.InnerException?.Message ?? ex.Message}"); }
            }

            var reset = t.GetMethod("ResetCard", All, null, Type.EmptyTypes, null)
                     ?? t.GetMethods(All).FirstOrDefault(m => m.Name == "ResetCard" && m.GetParameters().Length == 0);
            if (reset != null)
            {
                try { reset.Invoke(card, null); }
                catch (Exception ex) { Logger?.LogError($"[MakeBrine] ResetCard failed: {ex.InnerException?.Message ?? ex.Message}"); }
            }
        }

        private static bool IsMakeBrine(object action)
        {
            try
            {
                var name = Member(action, "ActionName");
                var key = Member(name, "LocalizationKey") as string;
                if (key == MakeBrineKey) return true;
                return (Member(name, "DefaultText") as string) == "Make Brine";
            }
            catch { return false; }
        }

        private static string GetUID(object card)
        {
            if (card == null) return null;
            try
            {
                var model = Member(card, "CardModel");
                return Member(model ?? card, "UniqueID") as string;
            }
            catch { return null; }
        }

        private static void InitReflection()
        {
            if (_reflInit) return;
            _reflInit = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_cardDataType == null) _cardDataType = asm.GetType("CardData", false);
                if (_uidType == null) _uidType = asm.GetType("UniqueIDScriptable", false);
            }
            if (_uidType != null && _cardDataType != null)
            {
                var g = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (g != null) _getFromID = g.MakeGenericMethod(_cardDataType);
            }
        }

        private static object Member(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = FindProperty(t, name);
            if (p?.CanRead == true) { try { return p.GetValue(obj, null); } catch { } }
            var f = FindField(t, name);
            if (f != null) { try { return f.GetValue(obj); } catch { } }
            return null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, All);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var prop = current.GetProperty(name, All);
                if (prop != null) return prop;
            }
            return null;
        }
    }
}
