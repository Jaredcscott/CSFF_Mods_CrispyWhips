using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Utility_Belt;

/// <summary>
/// [Display] Container Filter (plan B12). A search box on an open container's inventory popup: typing hides
/// every slot whose card's name does not contain the text, the way the blueprint screen's own search does.
/// [Hotkeys] Focus Container Filter puts the cursor in the box.
/// </summary>
/// <remarks>
/// <para>The box is a copy of the blueprint screen's own search field (<c>BlueprintModelsScreen.SearchInputField</c>),
/// so it has the game's look and font. The copy is made under an inactive holder so none of its scripts wake
/// before its events are replaced: every TMP_InputField event and every child Button's onClick is replaced
/// with an empty one (the original's persistent listeners call the blueprint screen), child
/// <c>LocalizedStaticText</c> and <c>EventTrigger</c> components are removed, and vanilla's
/// <c>TypingInInputField</c> is kept (added when missing). That component is the only writer of
/// <c>GraphicsManager.PlayerIsTyping</c>, so while the box has focus the game's hotkeys (A, S, Q, W, B, J, E,
/// H, C, T, D) and every Utility Belt key stay quiet. No raw key capture happens anywhere.</para>
/// <para>Filtering: <c>slot.IsActive = false</c> on the open container line's slots that do not match, as
/// <c>BlueprintModelsScreen.OnSearchStringValueChanged</c> does; the layout collapses inactive slots. The match
/// is a case-insensitive substring of <c>CardName()</c> in the current language (a plain IndexOf, so typed
/// characters are never read as a pattern). <c>InspectionPopup.SetupInventory</c> turns every slot back on
/// (on open, RefreshInventory and SortInventory), so its postfix re-applies the filter
/// (<see cref="Patcher.ContainerFilterPatch"/>, Priority.Low so it runs after the B3 readout postfix; the two
/// write different things). A change of text calls the public <c>RefreshInventory</c>, which runs
/// SetupInventory and so the postfix. Blueprint construction popups (which override SetupInventory) and
/// every popup other than <c>GraphicsManager.InventoryInspectionPopup</c> are left alone.</para>
/// <para>The text empties when the popup closes or another container opens; an empty box shows every slot.
/// The box is created once per popup instance (the popup is reused for every card) and shown or hidden per
/// open.</para>
/// </remarks>
internal static class ContainerFilter
{
    private const string FieldName = "UB_ContainerFilter";

    private static readonly HashSet<string> _warned = new();
    private static GraphicsManager _gm;
    private static InspectionPopup _owner;
    private static TMP_InputField _field;
    private static InGameCardBase _forCard;
    private static float _nextCreate;

    /// <summary>The current filter text ("" = no filter).</summary>
    internal static string Text { get; private set; } = string.Empty;

    /// <summary>The input field, for the harness; null until a container has been opened with the option on.</summary>
    internal static TMP_InputField Field => _field;

    internal static void Tick()
    {
        try
        {
            TickInner();
        }
        catch (Exception ex)
        {
            WarnOnce("tick:" + ex.GetType().Name, $"[UB] Container filter failed ({ex.GetType().Name}: {ex.Message}); the container popup is left as the game drew it.");
        }
    }

    private static void TickInner()
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        if (gm != _gm)
        {
            _gm = gm;
            _owner = null;
            _field = null;
            _forCard = null;
            Text = string.Empty;
        }

        if (gm == null)
        {
            return;
        }

        var popup = gm.InventoryInspectionPopup;
        var card = popup != null && popup.gameObject.activeInHierarchy ? popup.CurrentCard : null;
        if (!Plugin.ContainerFilterOn.Value || card == null || popup is BlueprintConstructionPopup)
        {
            Close();
            return;
        }

        if (card != _forCard)
        {
            // Another container opened in the same popup: start it unfiltered.
            _forCard = card;
            SetText(string.Empty, refresh: false);
        }

        if (_field == null || _owner != popup)
        {
            if (Time.unscaledTime < _nextCreate)
            {
                return;
            }

            _nextCreate = Time.unscaledTime + 1f;
            _owner = popup;
            _field = Create(gm, popup);
            if (_field == null)
            {
                return;
            }
        }

        if (!_field.gameObject.activeSelf)
        {
            _field.gameObject.SetActive(true);
        }

        var key = Plugin.FocusContainerFilterKey.Value;
        if (key != KeyCode.None && Input.GetKeyDown(key) && !GraphicsManager.PlayerIsTyping)
        {
            Focus();
        }
    }

    /// <summary>Puts the cursor in the box, as the blueprint screen does for its own search.</summary>
    internal static void Focus()
    {
        if (_field == null || !_field.gameObject.activeInHierarchy)
        {
            return;
        }

        EventSystem.current?.SetSelectedGameObject(_field.gameObject);
        _field.ActivateInputField();
    }

    /// <summary>Sets the filter text (the harness and the field's own change event both come here).</summary>
    internal static void SetText(string value, bool refresh = true)
    {
        value ??= string.Empty;
        var changed = !string.Equals(value, Text, StringComparison.Ordinal);
        Text = value;
        if (_field != null && _field.text != value)
        {
            _field.SetTextWithoutNotify(value);
        }

        if (refresh && changed && _owner != null && _owner.gameObject.activeInHierarchy && _owner.CurrentCard != null)
        {
            _owner.RefreshInventory(); // runs SetupInventory, whose postfix applies the filter
        }
    }

    private static void Close()
    {
        if (Text.Length > 0)
        {
            SetText(string.Empty); // shows every slot again if the popup is still open (the option was turned off)
        }

        if (_field != null && _field.gameObject.activeSelf)
        {
            _field.gameObject.SetActive(false); // TypingInInputField.OnDisable clears PlayerIsTyping
        }

        _forCard = null;
    }

    /// <summary>True when the card's name holds the filter text (always true for an empty filter).</summary>
    internal static bool Matches(InGameCardBase card, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (card == null || card.CardModel == null)
        {
            return false;
        }

        var name = card.CardName();
        return !string.IsNullOrEmpty(name) && name.IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Called from the SetupInventory postfix: hides the slots of the open container that do not match.</summary>
    internal static void Apply(InspectionPopup popup)
    {
        // popup.CurrentCard != _forCard: another container is being set up and the old text has not been cleared yet.
        if (Text.Length == 0 || popup == null || popup != _owner || popup is BlueprintConstructionPopup || !Plugin.ContainerFilterOn.Value
            || popup.CurrentCard == null || popup.CurrentCard != _forCard)
        {
            return;
        }

        var line = popup.InventorySlotsLine;
        if (line == null || line.Slots == null)
        {
            return;
        }

        foreach (var slot in line.Slots)
        {
            if (slot != null && slot.IsActive && !Matches(slot.AssignedCard, Text))
            {
                slot.IsActive = false;
            }
        }
    }

    private static TMP_InputField Create(GraphicsManager gm, InspectionPopup popup)
    {
        var parent = UnmaskedParent(popup);
        if (parent == null)
        {
            WarnOnce("no-parent", "[UB] Container filter: the inventory popup has no InventorySlotsParent; the filter box is not added.");
            return null;
        }

        var existing = parent.Find(FieldName);
        if (existing != null && existing.GetComponent<TMP_InputField>() is TMP_InputField found)
        {
            found.onValueChanged.AddListener(v => SetText(v));
            return found;
        }

        var screen = gm.BlueprintModelsPopup;
        var src = screen != null ? AccessTools.Field(typeof(BlueprintModelsScreen), "SearchInputField")?.GetValue(screen) as TMP_InputField : null;
        if (src == null)
        {
            WarnOnce("no-source", "[UB] Container filter: the blueprint screen's search field (BlueprintModelsScreen.SearchInputField) was not found; the filter box is not added.");
            return null;
        }

        var holder = new GameObject("UB_ContainerFilterHolder");
        holder.SetActive(false);
        try
        {
            var go = UnityEngine.Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = FieldName;
            var field = go.GetComponent<TMP_InputField>();

            // Every event the copy brought along calls the blueprint screen: replace them all before anything wakes.
            field.onValueChanged = new TMP_InputField.OnChangeEvent();
            field.onEndEdit = new TMP_InputField.SubmitEvent();
            field.onSubmit = new TMP_InputField.SubmitEvent();
            field.onSelect = new TMP_InputField.SelectionEvent();
            field.onDeselect = new TMP_InputField.SelectionEvent();
            field.onTextSelection = new TMP_InputField.TextSelectionEvent();
            field.onEndTextSelection = new TMP_InputField.TextSelectionEvent();
            foreach (var b in go.GetComponentsInChildren<Button>(true))
            {
                b.onClick = new Button.ButtonClickedEvent();
            }

            foreach (var t in go.GetComponentsInChildren<EventTrigger>(true))
            {
                UnityEngine.Object.DestroyImmediate(t);
            }

            foreach (var l in go.GetComponentsInChildren<LocalizedStaticText>(true))
            {
                UnityEngine.Object.DestroyImmediate(l);
            }

            if (go.GetComponent<TypingInInputField>() == null)
            {
                go.AddComponent<TypingInInputField>();
            }

            field.SetTextWithoutNotify(string.Empty);
            if (field.placeholder is TMP_Text placeholder)
            {
                placeholder.text = OverlayText.Get(OverlayText.ContainerFilterKey, OverlayText.ContainerFilterDefault);
            }

            field.onValueChanged.AddListener(v => SetText(v));

            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            // In the tab bar's free stretch, right of the Contents/Info/Recipes tabs and left of the bookmark and
            // close buttons (tuned on a 1280x720 rig screenshot, 2026-10-15: the box sits on the inventory panel's
            // top edge, 300 units in from its right).
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-300f, 26f);
            rt.sizeDelta = new Vector2(230f, 44f);
            rt.localScale = Vector3.one;

            // The copied field's colour tint fades its box to alpha 0 when not selected (the blueprint screen draws its
            // own frame behind it): no tint, and a parchment box with a dark rim so it reads on the popup.
            field.transition = Selectable.Transition.None;
            if (field.targetGraphic != null)
            {
                field.targetGraphic.color = new Color(0.98f, 0.95f, 0.87f, 1f);
                field.targetGraphic.CrossFadeAlpha(1f, 0f, true);
                var rim = field.targetGraphic.gameObject.GetComponent<Outline>() ?? field.targetGraphic.gameObject.AddComponent<Outline>();
                rim.effectColor = new Color(0.36f, 0.25f, 0.15f, 1f);
                rim.effectDistance = new Vector2(2f, -2f);
            }

            foreach (var t in new[] { field.textComponent, field.placeholder as TMP_Text })
            {
                if (t != null)
                {
                    t.enableAutoSizing = false;
                    t.fontSize = 26f;
                }
            }
            go.SetActive(true);
            field.targetGraphic?.CrossFadeAlpha(1f, 0f, true); // once active, so the fade is applied
            return field;
        }
        finally
        {
            UnityEngine.Object.Destroy(holder);
        }
    }

    /// <summary>
    /// The object to hang the box on: the parent of the outermost mask above the slot row, so the box is not clipped by
    /// the inventory's scroll viewport (measured: InventorySlots sits in a masked InventoryViewport inside Inventory).
    /// </summary>
    private static Transform UnmaskedParent(InspectionPopup popup)
    {
        Transform slots = popup.InventorySlotsParent;
        if (slots == null)
        {
            return null;
        }

        var result = slots;
        for (var t = slots; t != null && t != popup.transform; t = t.parent)
        {
            if ((t.GetComponent<Mask>() != null || t.GetComponent<RectMask2D>() != null) && t.parent != null)
            {
                result = t.parent;
            }
        }

        return result;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
