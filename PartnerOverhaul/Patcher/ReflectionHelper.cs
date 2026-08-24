namespace PartnerOverhaul.Patcher
{
    // NPCDutyOrDutyTagRef.Target is a private [SerializeField] on a struct — this is the only
    // place in this mod that genuinely needs reflection (every other lookup uses the plain,
    // non-nstrip Assembly-CSharp.dll's public compile-time types directly).
    internal static class ReflectionHelper
    {
        public static void SetPrivateField<TStruct>(ref TStruct target, string fieldName, object value)
            where TStruct : struct
        {
            var field = typeof(TStruct).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Logger.LogWarning($"[ReflectionHelper] {typeof(TStruct).Name}.{fieldName} not found — private field layout changed?");
                return;
            }
            object boxed = target;
            field.SetValue(boxed, value);
            target = (TStruct)boxed;
        }
    }
}
