using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Util;

/// <summary>
/// Shared reflection utility methods for accessing and modifying game object fields at runtime.
/// Used by ProducedCardService, mods, and other framework components.
/// </summary>
internal static class ReflectionHelpers
{
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Gets the value of a named field from an object using reflection.
    /// </summary>
    public static object GetFieldValue(object owner, string fieldName)
    {
        if (owner == null || string.IsNullOrEmpty(fieldName)) return null;
        var field = owner.GetType().GetField(fieldName, InstanceFlags);
        return field?.GetValue(owner);
    }

    /// <summary>
    /// Attempts to get an integer value from a field, handling int, long, short, and byte types.
    /// </summary>
    public static bool TryGetIntFieldValue(object owner, string fieldName, out int value)
    {
        value = 0;
        var raw = GetFieldValue(owner, fieldName);
        return TryGetIntValue(raw, out value);
    }

    /// <summary>
    /// Attempts to convert a value to an integer, handling int, long, short, and byte types.
    /// </summary>
    public static bool TryGetIntValue(object value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = (int)longValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Sets a field value if the field exists and the value type matches.
    /// </summary>
    public static bool SetFieldValueIfPresent(Type ownerType, object owner, string fieldName, object value)
    {
        var field = AccessTools.Field(ownerType, fieldName);
        if (field == null || value == null)
            return false;

        if (!field.FieldType.IsInstanceOfType(value))
            return false;

        field.SetValue(owner, value);
        return true;
    }

    /// <summary>
    /// Sets a numeric field value with type conversion if needed.
    /// </summary>
    public static bool SetNumericFieldIfPresent(Type ownerType, object owner, string fieldName, int value)
    {
        var field = AccessTools.Field(ownerType, fieldName);
        if (field == null)
            return false;

        try
        {
            object converted = field.FieldType.IsEnum
                ? Enum.ToObject(field.FieldType, value)
                : Convert.ChangeType(value, field.FieldType);
            field.SetValue(owner, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a Vector2, Vector2Int, or similar struct with x and y fields.
    /// </summary>
    public static object CreateVectorLike(Type vectorType, int x, int y)
    {
        if (vectorType == null)
            return null;

        try
        {
            if (vectorType == typeof(Vector2Int))
                return new Vector2Int(x, y);

            var instance = Activator.CreateInstance(vectorType);
            var xField = AccessTools.Field(vectorType, "x");
            var yField = AccessTools.Field(vectorType, "y");
            if (xField != null && yField != null)
            {
                xField.SetValue(instance, Convert.ChangeType(x, xField.FieldType));
                yField.SetValue(instance, Convert.ChangeType(y, yField.FieldType));
            }

            return instance;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Initializes all null fields in an object with their default values, recursively up to a depth limit.
    /// Used to ensure ProducedCards and other nested structures have proper initialized fields.
    /// </summary>
    public static int InitializeSerializableDefaults(object owner, int depth)
    {
        if (owner == null || depth < 0) return 0;

        int initialized = 0;
        foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.IsInitOnly) continue;

            var fieldType = field.FieldType;
            if (fieldType == typeof(string) || fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(decimal))
                continue;
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                continue;

            var currentValue = field.GetValue(owner);
            if (currentValue != null)
                continue;

            object replacement = null;
            if (fieldType.IsArray)
            {
                replacement = Array.CreateInstance(fieldType.GetElementType() ?? typeof(object), 0);
            }
            else if (!fieldType.IsAbstract && !fieldType.IsInterface)
            {
                try
                {
                    replacement = Activator.CreateInstance(fieldType);
                }
                catch
                {
                    replacement = null;
                }
            }

            if (replacement == null)
                continue;

            field.SetValue(owner, replacement);
            initialized++;

            if (depth > 0 && !(replacement is Array) && !(replacement is IList))
                initialized += InitializeSerializableDefaults(replacement, depth - 1);
        }

        return initialized;
    }
}
