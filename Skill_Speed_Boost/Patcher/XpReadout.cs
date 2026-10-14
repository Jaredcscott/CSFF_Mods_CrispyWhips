using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Util;
using UnityEngine;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// The XP readout toast (plan E3): a short floating message near the time wheel naming the XP bonuses
/// that applied to a skill gain, for example "Skill XP x1.50: morning, well rested". It is a DIAGNOSTIC,
/// not a bonus: it changes no XP. ChangeStat_Post builds a <see cref="Terms"/> only while
/// XpReadoutEnabled is on (one bool test when it is off, nothing allocated), notes a term beside each
/// composition line whose factor was not 1, and hands the result to <see cref="Notify"/> after the
/// gain is composed. A message is shown when the set of terms differs from the last one shown, or when
/// XpReadoutMinSeconds real seconds have passed with the same non-empty set. The game's own
/// GraphicsManager.PlayMessage shows it, anchored on GameGraphics.TimeSpentWheel with no card, the way
/// the game shows a stat interrupt; every member is resolved by reflection and every failure warns once
/// per cause and never throws. Under LogSkillXpGains a [Readout] Info line mirrors each message shown.
/// SSB ships no data file for this: no CardData, no GameStat, no localization (plan Open questions).
/// </summary>
internal static class XpReadout
{
    private static ManualLogSource Logger => Plugin.Logger;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    // The last term set shown, and when (Time.realtimeSinceStartup). Null until the first message, which
    // ShouldShow reads as an empty set, so a first gain with no bonus at all shows nothing.
    private static string _lastShownTerms;
    private static float _lastShownAt;
    private static bool _hasShown;

    private static FieldInfo _graphicsField;
    private static PropertyInfo _graphicsProperty;
    private static bool _graphicsResolved;
    private static MemberInfo _wheelMember;
    private static Type _wheelOwnerType;
    private static MethodInfo _playMessage;
    private static Type _playMessageOwnerType;

    /// <summary>
    /// The names of the composition terms that moved the multiplier for one gain, in composition order.
    /// Built only while XpReadoutEnabled is on. <see cref="Note"/> compares the running multiplier with the
    /// value it had at the previous note, so a term whose factor came out as 1 (no map entry, outside its
    /// window) is never named.
    /// </summary>
    internal sealed class Terms
    {
        private readonly List<string> _names = new();
        private float _mark = 1f;

        internal List<string> Names => _names;

        internal void Note(float multiplier, string name) => Note(multiplier, name, false);

        /// <summary>
        /// Names the term just applied when it changed the multiplier; with showFactor the term's own factor
        /// is appended ("area familiarity x1.10").
        /// </summary>
        internal void Note(float multiplier, string name, bool showFactor)
        {
            if (Math.Abs(multiplier - _mark) <= 0.0001f) return;
            string label = name;
            if (showFactor && Math.Abs(_mark) > 0.0001f)
                label = name + " x" + (multiplier / _mark).ToString("0.00", CultureInfo.InvariantCulture);
            _names.Add(label);
            _mark = multiplier;
        }
    }

    /// <summary>
    /// Called once per gain that reached composition, after the composed multiplier is final. Decides
    /// whether to show a message, mirrors it to the log under LogSkillXpGains, and shows it.
    /// </summary>
    internal static void Notify(string skill, float multiplier, Terms terms, float minSeconds)
    {
        if (terms == null) return;
        string key = JoinTerms(terms.Names);
        float now = Time.realtimeSinceStartup;
        double since = _hasShown ? now - _lastShownAt : double.MaxValue;
        if (!ShouldShow(_lastShownTerms, key, since, minSeconds)) return;

        _lastShownTerms = key;
        _lastShownAt = now;
        _hasShown = true;

        string message = FormatReadout(key, multiplier);
        if (Plugin.LogSkillXpGains)
            Logger?.LogInfo($"[Readout] {skill ?? "(unresolved skill)"}: {message}");
        TryShow(message);
    }

    /// <summary>
    /// The term names joined with ", ", the key a change of bonus set is measured on. Pure, C# 5, so
    /// SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it.
    /// </summary>
    internal static string JoinTerms(List<string> names)
    {
        if (names == null || names.Count == 0) return "";
        return string.Join(", ", names.ToArray());
    }

    /// <summary>
    /// The message text: "Skill XP x1.50: morning, well rested", or "Skill XP x1.00: no bonus" when the
    /// set is empty (shown only when the bonuses stop applying). Pure, C# 5.
    /// </summary>
    internal static string FormatReadout(string terms, float multiplier)
    {
        string list = string.IsNullOrEmpty(terms) ? "no bonus" : terms;
        return "Skill XP x" + multiplier.ToString("0.00", CultureInfo.InvariantCulture) + ": " + list;
    }

    /// <summary>
    /// True when a message should be shown: the term set differs from the last one shown (a null last set
    /// counts as empty, so a first gain with no bonus shows nothing), or the same non-empty set has gone
    /// minSeconds real seconds without one. An unchanged empty set is never repeated. Pure, C# 5.
    /// </summary>
    internal static bool ShouldShow(string lastTerms, string terms, double secondsSince, double minSeconds)
    {
        string last = lastTerms ?? "";
        string current = terms ?? "";
        if (!string.Equals(last, current, StringComparison.Ordinal)) return true;
        if (current.Length == 0) return false;
        return secondsSince >= minSeconds;
    }

    /// <summary>
    /// GraphicsManager.PlayMessage(Vector3, string, Transform, InGameCardBase) on the GameManager's
    /// GameGraphics, anchored on its TimeSpentWheel with a null card (.decomp/GameManager.cs, the stat
    /// interrupt notification). Each miss warns once per cause; nothing here throws.
    /// </summary>
    private static void TryShow(string message)
    {
        try
        {
            object gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                WarnOnce("gm-null", "no GameManager instance, so the XP readout message was not shown.");
                return;
            }

            object graphics = ReadGraphics(gm);
            if (graphics == null || (graphics is UnityEngine.Object graphicsObject && graphicsObject == null))
            {
                WarnOnce("graphics-null", "GameManager.GameGraphics is not set (yet), so the XP readout message was not shown.");
                return;
            }

            var graphicsType = graphics.GetType();
            if (_wheelOwnerType != graphicsType)
            {
                _wheelOwnerType = graphicsType;
                _wheelMember = (MemberInfo)graphicsType.GetField("TimeSpentWheel", Flags) ?? graphicsType.GetProperty("TimeSpentWheel", Flags);
            }
            if (_wheelMember == null)
            {
                WarnOnce("wheel-missing", $"{graphicsType.Name}.TimeSpentWheel not found, so the XP readout has no place to show. The game has probably renamed it.");
                return;
            }
            object wheel = _wheelMember is FieldInfo wheelField ? wheelField.GetValue(graphics) : ((PropertyInfo)_wheelMember).GetValue(graphics, null);
            if (!(wheel is Component wheelComponent) || wheelComponent == null)
            {
                WarnOnce("wheel-null", "the time wheel (GameGraphics.TimeSpentWheel) is not set, so the XP readout message was not shown.");
                return;
            }

            if (_playMessageOwnerType != graphicsType)
            {
                _playMessageOwnerType = graphicsType;
                _playMessage = FindPlayMessage(graphicsType);
            }
            if (_playMessage == null)
            {
                WarnOnce("playmessage-missing", $"{graphicsType.Name}.PlayMessage(Vector3, string, Transform, card) not found, so the XP readout cannot show. The game has probably changed it.");
                return;
            }

            Transform anchor = wheelComponent.transform;
            _playMessage.Invoke(graphics, new object[] { anchor.position, message, anchor, null });
        }
        catch (Exception ex)
        {
            WarnOnce("show-threw:" + ex.GetType().Name, $"showing the XP readout message failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static object ReadGraphics(object gm)
    {
        if (!_graphicsResolved)
        {
            _graphicsResolved = true;
            var gmType = gm.GetType();
            _graphicsField = gmType.GetField("GameGraphics", Flags);
            if (_graphicsField == null) _graphicsProperty = gmType.GetProperty("GameGraphics", Flags);
        }
        if (_graphicsField != null) return _graphicsField.GetValue(gm);
        if (_graphicsProperty != null) return _graphicsProperty.GetValue(gm, null);
        WarnOnce("graphics-missing", "GameManager.GameGraphics not found, so the XP readout cannot show. The game has probably renamed it.");
        return null;
    }

    private static MethodInfo FindPlayMessage(Type graphicsType)
    {
        foreach (var method in graphicsType.GetMethods(Flags))
        {
            if (method.Name != "PlayMessage") continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 4
                && parameters[0].ParameterType == typeof(Vector3)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(Transform)
                && !parameters[3].ParameterType.IsValueType)
                return method;
        }
        return null;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[Readout] {message}");
    }
}
