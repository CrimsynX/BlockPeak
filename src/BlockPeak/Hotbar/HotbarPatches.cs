using System;
using BlockPeak.Core;
using BlockPeak.Items;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;
using Zorro.Core.Serizalization;

namespace BlockPeak.Hotbar
{
    /// <summary>
    /// Hotbar positions vs PEAK slot ids. PEAK uses ids 0-2 for pockets, 3 for the backpack and 250 for the
    /// "hands full" slot, so the extra Minecraft slots use ids 4-9. Position p (0..8) maps to id p (p&lt;3) or p+1.
    /// Player.itemSlots grows to 3 + extra entries, ordered by position.
    /// </summary>
    public static class Slots
    {
        public const byte Backpack = 3;
        public const byte Temp = 250;

        public static int Count(Player p) => p != null && p.itemSlots != null ? p.itemSlots.Length : 3;
        public static byte IdForPosition(int pos) => (byte)(pos < 3 ? pos : pos + 1);

        public static int PositionForId(byte id)
        {
            if (id < 3) return id;
            if (id >= 4 && id <= 9) return id - 1;
            return -1;
        }
    }

    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Player_Awake_Patch
    {
        private static void Postfix(Player __instance)
        {
            int extra = Cfg.ExtraSlotCount;
            if (extra <= 0 || __instance.itemSlots == null || __instance.itemSlots.Length != 3) return;
            var arr = new ItemSlot[3 + extra];
            Array.Copy(__instance.itemSlots, arr, 3);
            for (int i = 3; i < arr.Length; i++) arr[i] = new ItemSlot(Slots.IdForPosition(i), __instance);
            __instance.itemSlots = arr;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetItemSlot))]
    internal static class Player_GetItemSlot_Patch
    {
        private static bool Prefix(Player __instance, byte slotID, ref ItemSlot __result)
        {
            if (slotID < 4 || slotID > 9) return true;
            int idx = slotID - 1;
            __result = idx < __instance.itemSlots.Length ? __instance.itemSlots[idx] : null;
            return false;
        }
    }

    /// <summary>Only steps in when the sender has a different number of slots (otherwise PEAK's own code runs).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SyncInventoryRPC))]
    internal static class Player_SyncInventoryRPC_Patch
    {
        private static bool Prefix(Player __instance, byte[] data, bool forceSync)
        {
            if (!forceSync && PhotonNetwork.IsMasterClient) return true;
            InventorySyncData d;
            try { d = IBinarySerializable.GetFromManagedArray<InventorySyncData>(data); }
            catch { return true; }
            if (d.slots == null || d.slots.Length == __instance.itemSlots.Length) return true;

            Health.Report("hotbar-size", $"Inventory from the host has {d.slots.Length} slots but you have {__instance.itemSlots.Length}. Everyone should use the same HotbarSlots setting.");
            for (int b = 0; b < __instance.itemSlots.Length; b++)
            {
                if (b < d.slots.Length)
                    __instance.itemSlots[b].SetItem(ItemDatabase.TryGetItem(d.slots[b].ItemID, out var it) ? it : null, d.slots[b].Data);
                else
                    __instance.itemSlots[b].EmptyOut();
            }
            __instance.backpackSlot.backpackType = (BackpackSlot.BackpackType)d.backpackType;
            __instance.backpackSlot.SetItem(ItemDatabase.TryGetItem(d.backpackSlot.ItemID, out var bp) ? bp : null, d.backpackInstanceData);
            __instance.tempFullSlot.SetItem(ItemDatabase.TryGetItem(d.tempSlot.ItemID, out var tmp) ? tmp : null, d.tempSlot.Data);
            var ch = __instance.character;
            if (ch != null)
            {
                if (__instance.view.IsMine) ch.refs.items.RefreshAllCharacterCarryWeightRPC();
                ch.refs.backpackHandler?.activeBackpackVisuals?.RefreshPocketBehaviors(__instance);
            }
            return false;
        }
    }

    /// <summary>Death / pass-out drops the extra slots too.</summary>
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropAllItems))]
    internal static class CharacterItems_DropAllItems_Patch
    {
        private static void Postfix(CharacterItems __instance)
        {
            var c = __instance.character;
            if (c == null || !c.IsLocal || c.player == null) return;
            Vector3 pos = c.GetBodypart(BodypartType.Hip).transform.position + Vector3.up * 2.2f;
            for (int i = 3; i < c.player.itemSlots.Length; i++)
            {
                var s = c.player.itemSlots[i];
                if (s.IsEmpty() || !s.prefab.UIData.canDrop) continue;
                __instance.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, s.itemSlotID, pos);
                pos += Vector3.up * 0.5f;
            }
        }
    }

    /// <summary>Picking up a Minecraft item tops up a matching stack first, like Minecraft.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.AddItem))]
    internal static class Player_AddItem_Patch
    {
        private static bool Prefix(Player __instance, ushort itemID, ItemInstanceData instanceData, ref ItemSlot slot, ref bool __result)
        {
            if (!PhotonNetwork.IsMasterClient || instanceData == null) return true;
            var def = ItemDefs.ById(itemID);
            if (def == null || def.Stack <= 1) return true;
            try
            {
                int incoming = Stacks.Count(instanceData);
                int max = def.Stack;
                ItemSlot last = null;
                foreach (var s in __instance.itemSlots)
                {
                    if (incoming <= 0) break;
                    if (s.IsEmpty() || s.prefab.itemID != itemID) continue;
                    int have = Stacks.Count(s.data);
                    if (have >= max) continue;
                    int moved = Math.Min(incoming, max - have);
                    Stacks.SetCount(s.data, have + moved, max);
                    incoming -= moved;
                    last = s;
                }
                if (last == null) return true;
                if (incoming > 0)
                {
                    Stacks.SetCount(instanceData, incoming, max);
                    return true; // the rest goes to a free slot as usual
                }
                slot = last;
                __result = true;
                byte[] array = IBinarySerializable.ToManagedArray(new InventorySyncData(__instance.itemSlots, __instance.backpackSlot, __instance.tempFullSlot));
                __instance.view.RPC("SyncInventoryRPC", RpcTarget.Others, array, false);
                return false;
            }
            catch (Exception e)
            {
                Health.Report("stack-merge", e);
                return true;
            }
        }
    }

    /// <summary>Stacks weigh more than single items.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateWeight))]
    internal static class CharacterAfflictions_UpdateWeight_Patch
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            var c = __instance.character;
            if (c == null || c.player == null) return;
            float extra = 0;
            void Add(ItemSlot s)
            {
                if (s == null || s.IsEmpty()) return;
                var def = ItemDefs.ById(s.prefab.itemID);
                if (def == null) return;
                extra += def.WeightFor(Stacks.Count(s.data)) - s.prefab.CarryWeight;
            }
            foreach (var s in c.player.itemSlots) Add(s);
            Add(c.player.tempFullSlot);
            var bp = c.player.backpackSlot;
            if (!bp.IsEmpty() && bp.data != null && bp.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out var bd))
                foreach (var s in bd.itemSlots) Add(s);
            if (Mathf.Abs(extra) < 0.001f) return;
            float cur = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Weight);
            __instance.SetStatus(CharacterAfflictions.STATUSTYPE.Weight, Mathf.Max(0f, cur + 0.025f * extra));
        }
    }

    /// <summary>
    /// Minecraft-style slot switching: number keys 1-9, the mouse wheel cycles through the hotbar and then the
    /// backpack. Replaces PEAK's DoSwitching only when the hotbar has more than 3 slots.
    /// </summary>
    [HarmonyPatch(typeof(CharacterItems), "DoSwitching")]
    internal static class CharacterItems_DoSwitching_Patch
    {
        private static bool Prefix(CharacterItems __instance)
        {
            if (Cfg.ExtraSlotCount <= 0) return true;
            if (UI.TestMenu.Open || UI.ChatBox.Open) return false;
            try { Switch(__instance); }
            catch (Exception e) { Health.Report("hotbar-switch", e); return true; }
            return false;
        }

        private static void Switch(CharacterItems items)
        {
            var c = items.character;
            if (items.timesSwitchedRecently > 0 && items.lastSwitched + 0.4f < Time.time) items.timesSwitchedRecently = 0;
            if ((c.data.currentItem != null && (c.data.currentItem.progress > 0f || c.data.currentItem.lastFinishedCast + 0.1f > Time.time))
                || !c.data.fullyConscious || !c.IsLocal || !c.CanDoInput() || items.lockedFromSwitching || c.data.isClimbing || c.data.isRopeClimbing)
                return;

            int count = Slots.Count(c.player);
            bool blocked = c.input.itemSwitchBlocked;

            // Number keys
            int pressed = -1;
            for (int p = 0; p < 9 && pressed < 0; p++)
            {
                bool hit = SafeHotbar(c.input, p);
                if (!hit && Cfg.RawNumberKeys.Value && !blocked) hit = DigitPressed(p);
                if (hit) pressed = p;
            }
            if (pressed >= count) pressed = -1;

            bool backpackKey = !blocked && ((Cfg.BackpackKey.Value != KeyCode.None && Input_GetKeyDown(Cfg.BackpackKey.Value)) || (pressed < 0 && c.input.selectBackpackWasPressed));

            // Wheel / bumpers
            int dir = 0;
            if (c.input.selectSlotForwardWasPressed) dir = 1;
            else if (c.input.selectSlotBackwardWasPressed) dir = -1;
            else if (Cfg.MouseWheelSwitchesSlots.Value && !blocked && !AltHeld() && !(c.data.currentItem != null && c.data.currentItem.UIData.hasScrollingInteract))
            {
                float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                if (wheel < -0.01f) dir = 1;
                else if (wheel > 0.01f) dir = -1;
            }

            if (pressed >= 0 || backpackKey || dir != 0 || c.input.unselectSlotWasPressed)
            {
                if (c.data.currentStickyItem)
                {
                    c.refs.animations.throwTime = 0.125f;
                    return;
                }
            }

            if (pressed >= 0)
            {
                byte id = Slots.IdForPosition(pressed);
                Equip(items, (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == id) ? Optionable<byte>.None : Optionable<byte>.Some(id));
                return;
            }
            if (backpackKey)
            {
                Equip(items, (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == Slots.Backpack) ? Optionable<byte>.None : Optionable<byte>.Some(Slots.Backpack));
                if (c.data.carriedPlayer != null) c.refs.carriying.Drop(c.data.carriedPlayer);
                return;
            }
            if (dir != 0)
            {
                // Order: hotbar positions 0..count-1, then the backpack.
                int cur;
                var last = items.currentSelectedSlot.IsSome ? items.currentSelectedSlot : items.lastSelectedSlot;
                if (last.IsNone || last.Value == Slots.Temp) cur = -1;
                else if (last.Value == Slots.Backpack) cur = count;
                else cur = Slots.PositionForId(last.Value);
                int n = count + 1;
                int next = ((cur < 0 ? (dir > 0 ? -1 : 0) : cur) + dir + n) % n;
                byte id = next == count ? Slots.Backpack : Slots.IdForPosition(next);
                Equip(items, Optionable<byte>.Some(id));
                return;
            }
            if (c.input.unselectSlotWasPressed)
            {
                Equip(items, items.currentSelectedSlot.IsSome ? Optionable<byte>.None : items.lastSelectedSlot);
            }
        }

        private static void Equip(CharacterItems items, Optionable<byte> slot)
        {
            items.lastSwitched = Time.time;
            items.timesSwitchedRecently++;
            items.EquipSlot(slot);
        }

        private static bool SafeHotbar(CharacterInput input, int p)
        {
            try
            {
                var a = CharacterInput.hotbarActions;
                if (a == null || p >= a.Length || a[p] == null) return false;
                return input.HotbarKeyWasPressed(p);
            }
            catch { return false; }
        }

        private static readonly Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

        private static bool DigitPressed(int p)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb[digitKeys[p]].wasPressedThisFrame || (Cfg.RawNumberKeys.Value && kb[(Key)((int)Key.Numpad1 + p)].wasPressedThisFrame);
        }

        private static bool AltHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
        }

        internal static bool Input_GetKeyDown(KeyCode k) => KeyInput.Down(k);
    }

    /// <summary>KeyCode -> new Input System key (PEAK has the old input manager disabled).</summary>
    public static class KeyInput
    {
        public static bool Down(KeyCode k)
        {
            var kb = Keyboard.current;
            if (kb == null || k == KeyCode.None) return false;
            var key = Map(k);
            return key != Key.None && kb[key].wasPressedThisFrame;
        }

        public static bool Held(KeyCode k)
        {
            var kb = Keyboard.current;
            if (kb == null || k == KeyCode.None) return false;
            var key = Map(k);
            return key != Key.None && kb[key].isPressed;
        }

        public static Key Map(KeyCode k)
        {
            if (k >= KeyCode.A && k <= KeyCode.Z) return (Key)((int)Key.A + (k - KeyCode.A));
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return (Key)((int)Key.Digit1 + (k - KeyCode.Alpha1));
            if (k == KeyCode.Alpha0) return Key.Digit0;
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return (Key)((int)Key.Numpad0 + (k - KeyCode.Keypad0));
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return (Key)((int)Key.F1 + (k - KeyCode.F1));
            switch (k)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Backslash: return Key.Backslash;
                case KeyCode.CapsLock: return Key.CapsLock;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Insert: return Key.Insert;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Mouse3: return Key.None;
                default: return Key.None;
            }
        }
    }
}
