using System;
using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPeak.Hotbar
{
    /// <summary>
    /// Draws Minecraft's hotbar (with Minecraft's own sprites and font) in place of PEAK's item boxes.
    /// PEAK's boxes keep running underneath, just invisible, so nothing that depends on them breaks.
    /// The backpack is shown in the off-hand slot on the left.
    /// </summary>
    public class HotbarHud
    {
        private class SlotView
        {
            public RawImage Icon;
            public RawImage Count;
            public RawImage BarBack;
            public RawImage BarFill;
            public Texture LastIcon;
            public string LastCount;
        }

        private GameObject root;
        private RectTransform bar;
        private RawImage background, selection, offhand, nameImage;
        private readonly List<SlotView> slots = new List<SlotView>();
        private SlotView offhandSlot;
        private int builtFor = -1, builtScale = -1, builtGen = -1;
        private GUIManager hiddenFor;
        private float nameShownAt = -10f;
        private string lastName = "";
        private int lastSelected = -2;

        public bool Visible => root != null && root.activeSelf;

        public void Update()
        {
            try { Tick(); }
            catch (Exception e)
            {
                Health.Report("hotbar-hud", e);
                if (root != null) root.SetActive(false);
            }
        }

        private int GuiScale()
        {
            int s = Cfg.HotbarScale.Value;
            if (s > 0) return s;
            int auto = 1;
            while (auto < 4 && Screen.width >= 320 * (auto + 1) && Screen.height >= 240 * (auto + 1)) auto++;
            return auto;
        }

        private void Tick()
        {
            var gm = GUIManager.instance;
            var me = Character.localCharacter;
            var watched = Character.observedCharacter;
            bool show = gm != null && me != null && watched != null && watched.player != null && gm.hudCanvas != null && gm.hudCanvas.enabled
                        && (gm.hudCanvasGroup == null || gm.hudCanvasGroup.alpha > 0.05f);

            if (gm != null && hiddenFor != gm) HideVanilla(gm);
            if (!show)
            {
                if (root != null) root.SetActive(false);
                return;
            }

            int count = Slots.Count(watched.player);
            int scale = GuiScale();
            if (root == null || builtFor != count || builtScale != scale || builtGen != McAssets.Generation) Build(count, scale);
            root.SetActive(true);
            if (gm.hudCanvasGroup != null)
            {
                var cg = root.GetComponent<CanvasGroup>();
                cg.alpha = gm.hudCanvasGroup.alpha;
            }

            var items = watched.refs.items;
            int selectedPos = -1;
            bool backpackSelected = false;
            if (items.currentSelectedSlot.IsSome)
            {
                byte id = items.currentSelectedSlot.Value;
                if (id == Slots.Backpack) backpackSelected = true;
                else selectedPos = Slots.PositionForId(id);
            }

            for (int p = 0; p < count; p++)
                Fill(slots[p], watched.player.itemSlots[p], watched);
            FillBackpack(offhandSlot, watched);

            selection.gameObject.SetActive(selectedPos >= 0 || backpackSelected);
            if (selectedPos >= 0) Place(selection.rectTransform, -1 + 20 * selectedPos, -1, 24, 23, scale);
            else if (backpackSelected) Place(selection.rectTransform, -30, -1, 24, 23, scale);

            // Item name pops up above the hotbar when the selection changes (Minecraft does the same).
            int selKey = backpackSelected ? 99 : selectedPos;
            string name = "";
            if (selectedPos >= 0) { var s = watched.player.itemSlots[selectedPos]; if (!s.IsEmpty()) name = s.prefab.GetItemName(s.data); }
            else if (backpackSelected && !watched.player.backpackSlot.IsEmpty()) name = watched.player.backpackSlot.prefab.GetItemName(watched.player.backpackSlot.data);
            if (selKey != lastSelected || name != lastName)
            {
                lastSelected = selKey;
                lastName = name;
                nameShownAt = Time.unscaledTime;
                var t = string.IsNullOrEmpty(name) ? null : McFont.Render(Clean(name), Color.white);
                nameImage.texture = t;
                if (t != null)
                {
                    float w = t.width * scale, h = t.height * scale;
                    var rt = nameImage.rectTransform;
                    rt.sizeDelta = new Vector2(w, h);
                    rt.anchoredPosition = new Vector2((bar.sizeDelta.x - w) / 2f, bar.sizeDelta.y + 8 * scale);
                }
            }
            float age = Time.unscaledTime - nameShownAt;
            float alpha = nameImage.texture == null ? 0f : Mathf.Clamp01((2.5f - age) / 0.5f);
            nameImage.color = new Color(1, 1, 1, alpha);
            nameImage.gameObject.SetActive(alpha > 0f);
        }

        private static string Clean(string s)
        {
            // Strip TMP rich text tags PEAK sometimes puts in names.
            var sb = new System.Text.StringBuilder();
            bool tag = false;
            foreach (char c in s)
            {
                if (c == '<') { tag = true; continue; }
                if (c == '>') { tag = false; continue; }
                if (!tag) sb.Append(c);
            }
            return sb.ToString();
        }

        private void Fill(SlotView v, ItemSlot s, Character owner)
        {
            if (s == null || s.IsEmpty())
            {
                v.Icon.enabled = false;
                v.Count.enabled = false;
                v.BarBack.enabled = v.BarFill.enabled = false;
                v.LastIcon = null;
                return;
            }
            var def = ItemDefs.ById(s.prefab.itemID);
            Texture icon = s.prefab.UIData.GetIcon();
            if (icon != v.LastIcon) { v.Icon.texture = icon; v.LastIcon = icon; }
            v.Icon.enabled = icon != null;
            v.Icon.color = Color.white;
            if (s.data != null && s.data.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cooked) && cooked.Value > 0)
                v.Icon.color = ItemCooking.GetCookColor(cooked.Value);

            // Count (Minecraft items) or PEAK's "uses left" for items that have uses.
            string countText = null;
            if (def != null && def.Stack > 1)
            {
                int n = Stacks.Count(s.data);
                if (n > 1) countText = n.ToString();
            }
            else if (def == null && s.data != null && s.data.TryGetDataEntry<OptionableIntItemData>(DataEntryKey.ItemUses, out var uses) && uses.HasData && uses.Value > 1 && !s.prefab.UIData.hideFuel)
            {
                countText = uses.Value.ToString();
            }
            SetCount(v, countText);

            // Durability bar: elytra, or PEAK fuel/charge items (lantern, etc.).
            float frac = -1f;
            if (def != null && def.Kind == McKind.Elytra) frac = Stacks.Durability(s.data, 0.12f);
            else if (def == null && Character.observedCharacter == Character.localCharacter && s.data != null && !s.prefab.UIData.hideFuel
                     && s.data.TryGetDataEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage, out var pct) && pct.Value < 0.999f)
                frac = pct.Value;
            if (frac >= 0f)
            {
                v.BarBack.enabled = v.BarFill.enabled = true;
                var rt = v.BarFill.rectTransform;
                float sc = builtScale;
                rt.sizeDelta = new Vector2(Mathf.Max(0, Mathf.Round(13 * frac)) * sc, 1 * sc);
                v.BarFill.color = Color.HSVToRGB(Mathf.Clamp01(frac) / 3f, 1f, 1f);
            }
            else v.BarBack.enabled = v.BarFill.enabled = false;
        }

        private void FillBackpack(SlotView v, Character owner)
        {
            var bp = owner.player.backpackSlot;
            if (owner.data.carriedPlayer != null)
            {
                v.Icon.enabled = true;
                v.Icon.texture = GUIManager.instance.backpack != null ? GUIManager.instance.backpack.carryingIcon : null;
                v.Icon.color = owner.data.carriedPlayer.refs.customization.PlayerColor;
                SetCount(v, null);
                v.BarBack.enabled = v.BarFill.enabled = false;
                return;
            }
            if (bp.IsEmpty())
            {
                v.Icon.enabled = false;
                SetCount(v, null);
                v.BarBack.enabled = v.BarFill.enabled = false;
                return;
            }
            v.Icon.enabled = true;
            v.Icon.color = Color.white;
            v.Icon.texture = bp.prefab.UIData.GetIcon();
            int filled = 0;
            if (bp.data != null && bp.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out var bd)) filled = bd.FilledSlotCount();
            SetCount(v, filled > 0 ? filled.ToString() : null);
            v.BarBack.enabled = v.BarFill.enabled = false;
        }

        private void SetCount(SlotView v, string text)
        {
            if (text == v.LastCount) return;
            v.LastCount = text;
            if (string.IsNullOrEmpty(text)) { v.Count.enabled = false; return; }
            var t = McFont.Render(text, Color.white);
            if (t == null)
            {
                v.Count.enabled = false; // no font copied; PEAK's own text would look out of place
                return;
            }
            v.Count.texture = t;
            v.Count.enabled = true;
            float sc = builtScale;
            // Minecraft: x + 19 - 2 - width, y + 6 + 3 (relative to the 16x16 item)
            var rt = v.Count.rectTransform;
            rt.sizeDelta = new Vector2(t.width * sc, t.height * sc);
            rt.anchoredPosition = new Vector2((17 - (t.width - 1)) * sc, (16 - 9 - t.height + 1) * sc);
        }

        private void HideVanilla(GUIManager gm)
        {
            hiddenFor = gm;
            void Hide(Component c)
            {
                if (c == null) return;
                var cg = c.GetComponent<CanvasGroup>() ?? c.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            if (gm.items != null) foreach (var i in gm.items) Hide(i);
            Hide(gm.backpack);
        }

        private void Build(int count, int scale)
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            slots.Clear();
            builtFor = count;
            builtScale = scale;
            builtGen = McAssets.Generation;

            root = new GameObject("BlockPeak.Hotbar");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            canvas.pixelPerfect = true;
            root.AddComponent<CanvasGroup>().blocksRaycasts = false;

            var barGo = new GameObject("bar", typeof(RectTransform));
            barGo.transform.SetParent(root.transform, false);
            bar = (RectTransform)barGo.transform;
            int widthPx = 2 + 20 * count;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0f, 0f);
            bar.sizeDelta = new Vector2(widthPx * scale, 22 * scale);
            bar.anchoredPosition = new Vector2(-(widthPx * scale) / 2f, 1 * scale + Cfg.HotbarOffsetY.Value);

            background = Img(bar, "background", CroppedHotbar(count));
            Place(background.rectTransform, 0, 0, widthPx, 22, scale);

            offhand = Img(bar, "offhand", McAssets.Tex("gui/sprites/hud/hotbar_offhand_left.png"));
            Place(offhand.rectTransform, -29, -1, 29, 24, scale);
            offhandSlot = MakeSlot(bar, -26, 3, scale);

            for (int i = 0; i < count; i++) slots.Add(MakeSlot(bar, 3 + 20 * i, 3, scale));

            var selTex = McAssets.Tex("gui/sprites/hud/hotbar_selection.png");
            selection = Img(bar, "selection", selTex);
            Place(selection.rectTransform, -1, -1, 24, selTex.height == 24 ? 24 : 23, scale);

            nameImage = Img(bar, "name", null);
            nameImage.rectTransform.pivot = Vector2.zero;
            nameImage.rectTransform.anchorMin = nameImage.rectTransform.anchorMax = Vector2.zero;
            lastSelected = -2;
        }

        /// <summary>hotbar.png is drawn for 9 slots; cut it down for fewer.</summary>
        private static Texture2D CroppedHotbar(int count)
        {
            var src = McAssets.Tex("gui/sprites/hud/hotbar.png");
            if (count >= 9) return src;
            try
            {
                int w = 2 + 20 * count;
                var dst = new Texture2D(w, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int x = 0; x < w - 1; x++) dst.SetPixels(x, 0, 1, src.height, src.GetPixels(x, 0, 1, src.height));
                dst.SetPixels(w - 1, 0, 1, src.height, src.GetPixels(src.width - 1, 0, 1, src.height));
                dst.Apply();
                return dst;
            }
            catch { return src; }
        }

        private SlotView MakeSlot(RectTransform parent, int x, int yTop, int scale)
        {
            var holder = new GameObject("slot", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var rt = (RectTransform)holder.transform;
            Place(rt, x, yTop, 16, 16, scale);
            var v = new SlotView
            {
                Icon = Img(rt, "icon", null),
                BarBack = Img(rt, "barBack", Texture2D.whiteTexture),
                BarFill = Img(rt, "barFill", Texture2D.whiteTexture),
                Count = Img(rt, "count", null),
            };
            Stretch(v.Icon.rectTransform);
            v.BarBack.color = Color.black;
            SetBottomLeft(v.BarBack.rectTransform, 2 * scale, 1 * scale, 13 * scale, 2 * scale);
            SetBottomLeft(v.BarFill.rectTransform, 2 * scale, 2 * scale, 13 * scale, 1 * scale);
            var c = v.Count.rectTransform;
            c.anchorMin = c.anchorMax = Vector2.zero;
            c.pivot = Vector2.zero;
            v.Icon.enabled = v.Count.enabled = v.BarBack.enabled = v.BarFill.enabled = false;
            return v;
        }

        private static RawImage Img(Transform parent, string name, Texture tex)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void SetBottomLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Position in Minecraft GUI pixels measured from the hotbar's top-left corner.</summary>
        private void Place(RectTransform rt, float x, float yTop, float w, float h, int scale)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w * scale, h * scale);
            rt.anchoredPosition = new Vector2(x * scale, (22 - yTop - h) * scale);
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }
    }
}
