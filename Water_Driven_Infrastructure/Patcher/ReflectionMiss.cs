using System;
using System.Collections.Generic;
using System.Reflection;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// The one place a reflection miss on a game member is reported (audit W3, 2026-11-12).
    ///
    /// GetField, GetProperty, GetMethod and AccessTools return null for a member the game renamed or
    /// removed. Nothing throws, a null-conditional carries the null forward, and the feature that read
    /// the member stops with no line in the log. Every by-name lookup of a game member in Patcher/*.cs
    /// goes through this class (or a warn-once of its own that names the member), so a miss logs ONE
    /// warning per type.member for the session, naming both, and never again: no per-frame or per-card
    /// spam. A lookup that finds its member returns it untouched, so nothing changes while the game's
    /// names stay as they are.
    ///
    /// What is NOT a miss, and stays silent: a null INSTANCE (the caller's own normal negative), and a
    /// member that exists and holds null.
    ///
    /// Gate: Development_Tools/Tests/WDI-WarningSweep-2026-11.Tests.ps1 (a source-shape check). Two
    /// lookups keep a written-out shape because contracts in WaterDrivenInfrastructure.Tests.ps1 read
    /// their text: GetDataBaseAllData in ActionInterceptPatch and the affect step in MillDutyPatch.
    /// </summary>
    internal static class ReflectionMiss
    {
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when this type.member has already been reported (or was just now).</summary>
        internal static bool Warn(string typeName, string memberName, string consequence = null)
        {
            string key = (typeName ?? "?") + "." + (memberName ?? "?");
            lock (_warned)
            {
                if (!_warned.Add(key)) return true;
            }
            Plugin.Logger?.LogWarning(Describe(typeName, memberName, consequence));
            return true;
        }

        internal static bool Warn(Type type, string memberName, string consequence = null)
            => Warn(type?.Name, memberName, consequence);

        /// <summary>The warning text. Pure, so the wording is one decision.</summary>
        internal static string Describe(string typeName, string memberName, string consequence)
        {
            string tail = string.IsNullOrEmpty(consequence) ? "the feature that reads it is off" : consequence;
            return $"[WDI] Reflection miss: {typeName ?? "?"}.{memberName ?? "?"} was not found; {tail}. "
                 + "A game or framework update may have renamed it. [logged once per session]";
        }

        /// <summary>
        /// Passes <paramref name="found"/> through. When it is null and <paramref name="type"/> is known,
        /// reports type.member once. A null <paramref name="type"/> means the lookup never ran because
        /// something upstream was missing, which that upstream miss already reported.
        /// </summary>
        internal static T Require<T>(T found, Type type, string memberName, string consequence = null) where T : class
        {
            if (found == null && type != null) Warn(type.Name, memberName, consequence);
            return found;
        }

        /// <summary>The same for a lookup whose owner is known only by name (a type looked up by name).</summary>
        internal static T Require<T>(T found, string typeName, string memberName, string consequence = null) where T : class
        {
            if (found == null) Warn(typeName, memberName, consequence);
            return found;
        }

        /// <summary>The same when the caller writes owner and member as one "Type.member" text.</summary>
        internal static T RequireNamed<T>(T found, string qualifiedMember, string consequence = null) where T : class
        {
            if (found != null) return found;
            int dot = qualifiedMember == null ? -1 : qualifiedMember.LastIndexOf('.');
            if (dot <= 0) Warn("?", qualifiedMember, consequence);
            else Warn(qualifiedMember.Substring(0, dot), qualifiedMember.Substring(dot + 1), consequence);
            return null;
        }

        internal static FieldInfo Field(Type type, string name, BindingFlags flags, string consequence = null)
            => type == null ? null : Require(type.GetField(name, flags), type, name, consequence);

        internal static PropertyInfo Property(Type type, string name, BindingFlags flags, string consequence = null)
            => type == null ? null : Require(type.GetProperty(name, flags), type, name, consequence);

        internal static MethodInfo Method(Type type, string name, BindingFlags flags, string consequence = null)
            => type == null ? null : Require(type.GetMethod(name, flags), type, name, consequence);

        /// <summary>A field, else a property, of that name; one report when the type has neither.</summary>
        internal static MemberInfo FieldOrProperty(Type type, string name, BindingFlags flags, string consequence = null)
        {
            if (type == null) return null;
            var member = (MemberInfo)type.GetField(name, flags) ?? type.GetProperty(name, flags);
            return Require(member, type, name, consequence);
        }

        /// <summary>A game type looked up by name; reported as "(type).Name" when no loaded assembly has it.</summary>
        internal static Type GameType(Type found, string typeName, string consequence = null)
        {
            if (found == null) Warn("(type)", typeName, consequence);
            return found;
        }
    }
}
