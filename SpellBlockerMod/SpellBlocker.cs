using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SpellBlockerConfig : ModConfig
{
    [Header("General Settings")]
    [LabelText("Require Alt Pressed")]
    [Description("When enabled, Alt must be held down while pressing the slot hotkey to toggle spell lock.")]
    public bool requireAlt = true;

    [LabelText("Show Notifications")]
    [Description("Display on-screen notifications when locking or unlocking spells.")]
    public bool showNotifications = true;

    [Header("Debug")]
    [LabelText("[DEBUG] Don't disable identity memory effect on lock")]
    [Description("When enabled, Identity Memory passives (e.g. Chain Reaction on attack) continue running even while the spell is locked. Keep disabled to turn off passives on lock.")]
    public bool debugDontDisableIdentityEffect = false;

    // Default bindings: 1, 2, 3, 4, 5 (Dash), 6 (Identity), 0 (Unlock All)
    public DewBinding bindQ = DewBinding.KeyboardOnly(Key.Digit1);
    public DewBinding bindW = DewBinding.KeyboardOnly(Key.Digit2);
    public DewBinding bindE = DewBinding.KeyboardOnly(Key.Digit3);
    public DewBinding bindR = DewBinding.KeyboardOnly(Key.Digit4);
    public DewBinding bindDash = DewBinding.KeyboardOnly(Key.Digit5);
    public DewBinding bindIdentity = DewBinding.KeyboardOnly(Key.Digit6);
    public DewBinding bindUnlockAll = DewBinding.KeyboardOnly(Key.Digit0);

    public void EnsureDefaults()
    {
        if (bindQ == null || bindQ.pcBinds == null || bindQ.pcBinds.Count == 0) bindQ = DewBinding.KeyboardOnly(Key.Digit1);
        if (bindW == null || bindW.pcBinds == null || bindW.pcBinds.Count == 0) bindW = DewBinding.KeyboardOnly(Key.Digit2);
        if (bindE == null || bindE.pcBinds == null || bindE.pcBinds.Count == 0) bindE = DewBinding.KeyboardOnly(Key.Digit3);
        if (bindR == null || bindR.pcBinds == null || bindR.pcBinds.Count == 0) bindR = DewBinding.KeyboardOnly(Key.Digit4);
        if (bindDash == null || bindDash.pcBinds == null || bindDash.pcBinds.Count == 0) bindDash = DewBinding.KeyboardOnly(Key.Digit5);
        if (bindIdentity == null || bindIdentity.pcBinds == null || bindIdentity.pcBinds.Count == 0) bindIdentity = DewBinding.KeyboardOnly(Key.Digit6);
        if (bindUnlockAll == null || bindUnlockAll.pcBinds == null || bindUnlockAll.pcBinds.Count == 0) bindUnlockAll = DewBinding.KeyboardOnly(Key.Digit0);
    }

    public override void BuildWidgets(Transform parent, out SafeAction onChanged, out SafeAction requestUpdate)
    {
        EnsureDefaults();
        base.BuildWidgets(parent, out onChanged, out requestUpdate);

        var header = UnityEngine.Object.Instantiate(DewGUI.widgetTextHeader, parent);
        header.SetText("Key Bindings (Click button then press key to rebind)");

        CreateBindRow(parent, "Slot 1", bindQ, b => bindQ = b, onChanged);
        CreateBindRow(parent, "Slot 2", bindW, b => bindW = b, onChanged);
        CreateBindRow(parent, "Slot 3", bindE, b => bindE = b, onChanged);
        CreateBindRow(parent, "Slot 4", bindR, b => bindR = b, onChanged);
        CreateBindRow(parent, "Dash / Movement", bindDash, b => bindDash = b, onChanged);
        CreateBindRow(parent, "Identity", bindIdentity, b => bindIdentity = b, onChanged);
        CreateBindRow(parent, "Unlock All", bindUnlockAll, b => bindUnlockAll = b, onChanged);
    }

    private void CreateBindRow(Transform parent, string labelText, DewBinding currentBinding, Action<DewBinding> onNewBinding, SafeAction onChanged)
    {
        var row = DewGUI.CreateHorizontalLayoutGroup(parent, TextAnchor.MiddleLeft);
        DewGUI.ApplyLayoutGroupDefaultSettings(row, TextAnchor.MiddleLeft);

        var label = UnityEngine.Object.Instantiate(DewGUI.widgetTextLabel, row.transform);
        label.SetText(labelText);
        DewGUI.SetWidth(label.rectTransform, 260f);

        var bindItem = UnityEngine.Object.Instantiate(DewGUI.widgetBindItem, row.transform);
        bindItem.allowedTypes = BindingType.Keyboard;
        bindItem.allowModifiers = false;
        bindItem.UpdateValueDontNotify(currentBinding);
        bindItem.onValueChanged += (newVal) =>
        {
            onNewBinding(newVal);
            onChanged?.Invoke();
        };
    }
}

public class SpellBlocker : ModBehaviour
{
    public SpellBlockerConfig config = new SpellBlockerConfig();

    private readonly Dictionary<int, AbilityLockHandle> activeHandles = new Dictionary<int, AbilityLockHandle>();
    private readonly HashSet<SkillTrigger> disabledSkills = new HashSet<SkillTrigger>();
    private Hero currentHero;

    private static readonly MethodInfo OnUnequipMethod = typeof(AbilityTrigger).GetMethod("OnUnequip", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly MethodInfo OnEquipMethod = typeof(AbilityTrigger).GetMethod("OnEquip", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private void Awake()
    {
        config.EnsureDefaults();
        if (instance != null)
        {
            instance.isAlteringGameplay = true;
        }
        Debug.Log("SpellBlocker: [!] Mod Loaded Successfully.");
        try
        {
            if (config.showNotifications && GlobalUIManager.instance != null)
            {
                GlobalUIManager.instance.ShowDevText("[SpellBlocker] Mod Loaded", false);
            }
        }
        catch { }
    }

    private void Update()
    {
        try
        {
            if (ControlManager.IsInputFieldFocused())
                return;
        }
        catch { }

        bool alt = IsAltPressed();

        if (config.requireAlt && !alt)
        {
            return;
        }

        if (IsBindingPressed(config.bindQ))
            ToggleSlotLock(HeroSkillLocation.Q);
        else if (IsBindingPressed(config.bindW))
            ToggleSlotLock(HeroSkillLocation.W);
        else if (IsBindingPressed(config.bindE))
            ToggleSlotLock(HeroSkillLocation.E);
        else if (IsBindingPressed(config.bindR))
            ToggleSlotLock(HeroSkillLocation.R);
        else if (IsBindingPressed(config.bindDash))
            ToggleSlotLock(HeroSkillLocation.Movement);
        else if (IsBindingPressed(config.bindIdentity))
            ToggleSlotLock(HeroSkillLocation.Identity);
        else if (IsBindingPressed(config.bindUnlockAll))
            UnlockAll();
    }

    private void ToggleSlotLock(HeroSkillLocation loc)
    {
        var hero = GetLocalHero();
        if (hero == null || hero.Ability == null)
        {
            Debug.LogWarning("SpellBlocker: No local hero found in game.");
            return;
        }

        // Reset old handles if character changes (new run)
        if (currentHero != hero)
        {
            CleanupHandles();
            currentHero = hero;
        }

        int slotIndex = (int)loc;

        if (activeHandles.TryGetValue(slotIndex, out var handle))
        {
            // Remove lock
            if (handle != null)
            {
                try { handle.Stop(); } catch (Exception e) { Debug.LogException(e); }
            }
            activeHandles.Remove(slotIndex);

            // Restore passive effect on Identity Memory unlock
            if (loc == HeroSkillLocation.Identity && !config.debugDontDisableIdentityEffect)
            {
                var skill = hero.Skill?.GetSkill(HeroSkillLocation.Identity);
                SetSkillEffectsEnabled(skill, hero, true);
            }

            ShowFeedback($"{GetSlotDisplayName(loc)} Unlocked");
        }
        else
        {
            // Set lock
            AbilityLockHandle newHandle = null;
            try
            {
                newHandle = hero.Ability.GetNewAbilityLockHandle(true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (newHandle != null)
            {
                newHandle.LockAbilityCast(slotIndex);
                activeHandles[slotIndex] = newHandle;

                // Disable passive effects on Identity Memory lock
                if (loc == HeroSkillLocation.Identity && !config.debugDontDisableIdentityEffect)
                {
                    var skill = hero.Skill?.GetSkill(HeroSkillLocation.Identity);
                    SetSkillEffectsEnabled(skill, hero, false);
                }

                ShowFeedback($"{GetSlotDisplayName(loc)} Locked");
            }
            else
            {
                Debug.LogWarning($"SpellBlocker: Failed to get AbilityLockHandle for {loc}.");
                ShowFeedback($"Failed to lock {GetSlotDisplayName(loc)} (Host/Server required)");
            }
        }
    }

    private static string GetSlotDisplayName(HeroSkillLocation loc)
    {
        switch (loc)
        {
            case HeroSkillLocation.Q: return "Slot 1";
            case HeroSkillLocation.W: return "Slot 2";
            case HeroSkillLocation.E: return "Slot 3";
            case HeroSkillLocation.R: return "Slot 4";
            case HeroSkillLocation.Movement: return "Dash / Movement";
            case HeroSkillLocation.Identity: return "Identity";
            default: return loc.ToString();
        }
    }

    private void SetSkillEffectsEnabled(SkillTrigger skill, Hero hero, bool enabled)
    {
        if (skill == null || hero == null) return;

        try
        {
            if (enabled)
            {
                if (disabledSkills.Contains(skill))
                {
                    skill.enabled = true;
                    OnEquipMethod?.Invoke(skill, new object[] { hero });
                    disabledSkills.Remove(skill);
                }
            }
            else
            {
                OnUnequipMethod?.Invoke(skill, new object[] { hero });
                skill.enabled = false;
                disabledSkills.Add(skill);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void UnlockAll()
    {
        var hero = GetLocalHero();
        if (hero != null && hero.Skill != null && !config.debugDontDisableIdentityEffect)
        {
            var identitySkill = hero.Skill.GetSkill(HeroSkillLocation.Identity);
            SetSkillEffectsEnabled(identitySkill, hero, true);
        }

        CleanupHandles();
        ShowFeedback("All Spells Unlocked");
    }

    private Hero GetLocalHero()
    {
        try
        {
            if (ControlManager.instance != null && ControlManager.instance.controllingEntity is Hero cHero && cHero != null && cHero.isActiveAndEnabled)
                return cHero;
        }
        catch { }

        try
        {
            if (ActorManager.instance != null && ActorManager.instance.allHeroes != null)
                return ActorManager.instance.allHeroes.Find(h => h != null && h.isActiveAndEnabled && (h.isLocalPlayer || h.isOwned));
        }
        catch { }

        return null;
    }

    private bool IsAltPressed()
    {
        try
        {
            if (Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed))
                return true;
        }
        catch { }

        try
        {
            if (DewInput.GetButton(Key.LeftAlt) || DewInput.GetButton(Key.RightAlt))
                return true;
        }
        catch { }

        try
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return true;
        }
        catch { }

        return false;
    }

    private bool IsBindingPressed(DewBinding binding)
    {
        if (binding == null) return false;

        try
        {
            if (DewInput.GetButtonDown(binding, false))
                return true;
        }
        catch { }

        try
        {
            if (Keyboard.current != null && binding.pcBinds != null)
            {
                foreach (var pc in binding.pcBinds)
                {
                    if (pc.key != Key.None && Keyboard.current[pc.key].wasPressedThisFrame)
                        return true;
                }
            }
        }
        catch { }

        return false;
    }

    private void ShowFeedback(string msg)
    {
        Debug.Log($"SpellBlocker: {msg}");
        if (config == null || !config.showNotifications)
            return;

        try
        {
            if (GlobalUIManager.instance != null)
            {
                GlobalUIManager.instance.ShowDevText($"[SpellBlocker] {msg}", false);
            }
        }
        catch { }
    }

    private void CleanupHandles()
    {
        foreach (var handle in activeHandles.Values)
        {
            if (handle != null)
            {
                try { handle.Stop(); } catch { }
            }
        }
        activeHandles.Clear();

        // Restore skill effects on unlock
        if (currentHero != null)
        {
            foreach (var skill in disabledSkills)
            {
                if (skill != null)
                {
                    try
                    {
                        skill.enabled = true;
                        OnEquipMethod?.Invoke(skill, new object[] { currentHero });
                    }
                    catch { }
                }
            }
        }
        disabledSkills.Clear();
    }

    private void OnDestroy()
    {
        CleanupHandles();
    }
}
