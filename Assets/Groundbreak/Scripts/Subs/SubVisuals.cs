using UnityEngine;

namespace Groundbreak
{
    /// <summary>Company lettering on the sub's truck: a 24" x 18" magnetic door sign on each cab door (company color band, name, phone).</summary>
    public static class SubVisuals
    {
        public static void DoorSigns(Transform truck, string truckType, SubCompany co)
        {
            if (truck == null || co == null) return;
            Vector3 size = VehicleModels.SizeOf(truckType);
            float halfW = size.x * 0.5f - 0.2f;            // SizeOf includes mirrors
            float doorZ = size.z * 0.5f - (truckType == "van" ? 1.25f : 1.75f);
            float y = truckType == "box" || truckType == "dump" || truckType == "boom" ? 1.45f : 1.05f;
            foreach (int side in new[] { -1, 1 })
            {
                var root = new GameObject(side < 0 ? "DoorSignL" : "DoorSignR").transform;
                root.SetParent(truck, false);
                root.localPosition = new Vector3(side * (halfW + 0.012f), y, doorZ);
                root.localRotation = Quaternion.Euler(0, side * 90f, 0);   // +Z of the sign faces out of the door
                var k = new LandModel();
                float w = 24f * Units.Inch, h = 18f * Units.Inch;
                k.B.AddBox(Vector3.zero, new Vector3(w, h, 0.004f), k.M(SurfaceKind.PaintedWhite, new Color(0.96f, 0.96f, 0.94f)));
                k.B.AddBox(new Vector3(0, h * 0.5f - 0.035f, 0.003f), new Vector3(w, 0.07f, 0.002f), k.M(co.Color));
                k.B.AddBox(new Vector3(0, -h * 0.5f + 0.02f, 0.003f), new Vector3(w, 0.04f, 0.002f), k.M(co.Color));
                k.Make(root, "Panel");
                Text(root, co.Name, new Vector3(0, 0.03f, 0.006f), 0.0042f, w * 0.92f);
                Text(root, co.Phone + " · licensed & insured", new Vector3(0, -0.075f, 0.006f), 0.0028f, w * 0.92f);
            }
        }

        static void Text(Transform parent, string text, Vector3 local, float size, float maxWidth)
        {
            var go = new GameObject("Lettering");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0, 180f, 0);   // TextMesh reads along -Z; flip to face out
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontSize = 64; tm.characterSize = size; tm.color = new Color(0.08f, 0.1f, 0.15f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            // shrink long names to fit the panel
            var r = go.GetComponent<MeshRenderer>();
            float width = r.bounds.size.magnitude > 0f ? Mathf.Max(r.bounds.size.x, r.bounds.size.z) : 0f;
            if (width > maxWidth && width > 0f) tm.characterSize *= maxWidth / width;
        }
    }
}
