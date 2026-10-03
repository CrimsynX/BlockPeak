using System.Collections;
using BlockPeak.Assets;
using BlockPeak.UI;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>Draws and animates one mob (walk cycle, zombie arms, creeper swell, hurt flash, death).</summary>
    public class MobView : MonoBehaviour
    {
        public MobModel Model;
        private float walk;
        private float flashUntil;
        private float fireTick;
        private MaterialPropertyBlock mpb;
        private static readonly string[] ColorProps = { "_BaseColor", "_Color", "_Tint", "_MainColor" };
        private bool dying;

        public void Init(string type)
        {
            Model = MobModels.Build(type, transform);
            mpb = new MaterialPropertyBlock();
        }

        public void Apply(Vector3 pos, float yaw, bool moving, bool burning, bool fusing, float dt)
        {
            if (dying) return;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            walk += moving ? dt * 8f : 0f;
            float swing = moving ? Mathf.Sin(walk) : Mathf.Lerp(Mathf.Sin(walk), 0, 0.2f);
            float a = 35f * swing;

            switch (Model.Kind)
            {
                case "humanoid":
                case "skeleton":
                    if (Model.LegR != null) Model.LegR.localRotation = Quaternion.Euler(a, 0, 0);
                    if (Model.LegL != null) Model.LegL.localRotation = Quaternion.Euler(-a, 0, 0);
                    // Zombies hold their arms out; skeletons aim a little.
                    float armBase = Model.Kind == "humanoid" ? -90f : -80f;
                    if (Model.ArmR != null) Model.ArmR.localRotation = Quaternion.Euler(armBase + Mathf.Sin(Time.time * 2f) * 4f, 0, 0);
                    if (Model.ArmL != null) Model.ArmL.localRotation = Quaternion.Euler(armBase - Mathf.Sin(Time.time * 2f) * 4f, Model.Kind == "skeleton" ? 20f : 0, 0);
                    break;
                case "spider":
                    for (int i = 0; i < 4; i++)
                    {
                        float fan = (i - 1.5f) * 22f; // front legs forward, back legs back
                        float wiggle = Mathf.Sin(walk + i * 1.6f) * (moving ? 15f : 0f);
                        Model.SpiderLegsR[i].localRotation = Quaternion.Euler(0, -fan + wiggle, -35f);
                        Model.SpiderLegsL[i].localRotation = Quaternion.Euler(0, fan - wiggle, 35f);
                    }
                    break;
                case "creeper":
                    if (Model.QuadLegs.Count == 4)
                    {
                        Model.QuadLegs[0].localRotation = Quaternion.Euler(a, 0, 0);
                        Model.QuadLegs[1].localRotation = Quaternion.Euler(-a, 0, 0);
                        Model.QuadLegs[2].localRotation = Quaternion.Euler(-a, 0, 0);
                        Model.QuadLegs[3].localRotation = Quaternion.Euler(a, 0, 0);
                    }
                    float swell = fusing ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(Time.time * 10f)) : 1f;
                    Model.Root.transform.localScale = new Vector3(swell, 1f + (swell - 1f) * 0.5f, swell);
                    break;
                case "slime":
                case "magma":
                    float squish = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
                    Model.Root.transform.localScale = new Vector3(squish, 2f - squish, squish);
                    break;
            }

            bool flash = Time.time < flashUntil || (fusing && Mathf.Sin(Time.time * 20f) > 0.3f);
            Tint(flash ? (fusing && Time.time >= flashUntil ? new Color(1.6f, 1.6f, 1.6f) : new Color(1f, 0.35f, 0.35f)) : Color.white);

            if (burning && Time.time > fireTick)
            {
                fireTick = Time.time + 0.15f;
                Fx.Burst(pos + Vector3.up * Model.Height * Random.Range(0.2f, 0.9f), new[] { new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.2f) }, 2, 1f, 0.5f, false, 0.07f);
            }
        }

        private void Tint(Color c)
        {
            foreach (var r in Model.Renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                var m = r.sharedMaterial;
                foreach (var p in ColorProps) if (m != null && m.HasProperty(p)) mpb.SetColor(p, c);
                r.SetPropertyBlock(mpb);
            }
        }

        public void Flash() => flashUntil = Time.time + 0.3f;

        public void Die(byte reason)
        {
            if (dying) return;
            dying = true;
            StartCoroutine(DieRoutine(reason));
        }

        private IEnumerator DieRoutine(byte reason)
        {
            if (reason == 1)
            {
                // Minecraft's death: tip over sideways, then puff.
                Tint(new Color(1f, 0.35f, 0.35f));
                Quaternion start = transform.rotation;
                for (float t = 0; t < 0.8f; t += Time.deltaTime)
                {
                    transform.rotation = start * Quaternion.Euler(0, 0, Mathf.Lerp(0, 90f, t / 0.8f));
                    yield return null;
                }
            }
            if (reason != 2)
            {
                Fx.Burst(transform.position + Vector3.up * 0.5f, new[] { Color.white, new Color(0.8f, 0.8f, 0.8f) }, 14, 1.5f, 0.8f, false, 0.1f);
                Sfx.At("random/pop", transform.position, 0.3f);
            }
            Destroy(gameObject);
        }
    }
}
