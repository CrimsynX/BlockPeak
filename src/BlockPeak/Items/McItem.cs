using BlockPeak.Core;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>On every Minecraft item: rolls its stack size when found, and handles held light for torches.</summary>
    public class McItem : MonoBehaviour
    {
        public string key;

        [System.NonSerialized] public Item item;
        [System.NonSerialized] public McItemDef def;
        private Light heldLight;

        private void Awake()
        {
            item = GetComponent<Item>();
            def = ItemDefs.ByKey(key);
        }

        private void Start()
        {
            if (def == null || item == null) return;
            if (PhotonNetwork.IsMasterClient && item.itemState == ItemState.Ground)
            {
                item.GetData<BoolItemData>(Stacks.RolledKey); // makes sure data exists
                if (!Stacks.WasRolledFlag(item.data))
                {
                    var (min, max) = def.Find;
                    int n = Mathf.Clamp(Random.Range(min, max + 1), 1, def.Stack);
                    if (def.Stack > 1) Stacks.SetCount(item.data, n, def.Stack);
                    if (def.Kind == McKind.Elytra) Stacks.SetDurability(item.data, Balance.F(def.Cfg, "startDurability", 1f));
                    if (def.Kind == McKind.Bow) Stacks.SetArrows(item.data, Balance.I(def.Cfg, "arrows", 16));
                    Stacks.MarkRolled(item.data);
                }
            }
            if (def.Kind == McKind.Torch)
            {
                var vis = transform.Find("BP_Visual");
                var go = new GameObject("BP_TorchLight");
                go.transform.SetParent(vis != null ? vis : transform, false);
                go.transform.localPosition = new Vector3(0, 0.6f, 0);
                heldLight = go.AddComponent<Light>();
                heldLight.type = LightType.Point;
                bool red = false;
                heldLight.color = red ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.78f, 0.45f);
                heldLight.range = Balance.F(Balance.Section("building"), "torchLightRange", 8f) * (red ? 0.5f : 1f);
                heldLight.intensity = red ? 1.2f : 2.2f;
                heldLight.shadows = LightShadows.None;
            }
        }

        private void Update()
        {
            if (heldLight != null) heldLight.enabled = item.itemState != ItemState.InBackpack;
        }
    }
}
