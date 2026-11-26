using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Detailed 200 A main-breaker load center (14" x 30" x 4" can, panel-local inches, front = +Z):
    /// painted can with concentric knockouts, NM cable connectors + locknuts at used knockouts, insulating base with bus legs and
    /// alternating stabs, breaker rails, 200 A main with line lugs and load straps, branch breakers with amp-rated handles,
    /// load lugs, AFCI/GFCI test buttons, handle ties on 2-pole, insulated neutral bars with a green bonding screw + crossover strap,
    /// a bonded ground bar with the grounding electrode conductor, landed conductors with the cable sheath stripped just inside.
    /// With ElPanel.Cover the dead-front goes on: breaker windows only where breakers sit (twist-out fillers elsewhere),
    /// printed space numbers, nameplate, cover screws and a hinged door (open) with the circuit directory filled in.</summary>
    public static class ElPanelLook
    {
        static float I(float x) => Units.In(x);
        static Vector3 V(float x, float y, float z) => new Vector3(I(x), I(y), I(z));

        const int Gray = 0, Galv = 1, Dark = 2, Tin = 3, White = 4, Red = 5, Seam = 6, Green = 7, Yellow = 8, Card = 9;
        static readonly Color Ink = new Color(0.08f, 0.08f, 0.09f);
        static readonly Color Pen = new Color(0.08f, 0.10f, 0.32f);

        static Material[] Mats => new[]
        {
            ElVisuals.PanelGray, ElVisuals.Steel, ElVisuals.Black, ElVisuals.Nickel, ElVisuals.DeviceWhite, ElVisuals.RedNut,
            Materials.Get(SurfaceKind.PaintedSteel, new Color(0.36f, 0.37f, 0.39f)), ElVisuals.GreenNut, ElVisuals.YellowNut,
            Materials.Get(SurfaceKind.PaintedWhite, new Color(1.0f, 0.98f, 0.92f)),
        };

        public const float BusTopFromTop = 9f;
        public static float FaceZ(ElPanel p) => p.Piece.Size.z / Units.Inch * 0.5f - 0.12f;

        static GameObject Spawn(Transform parent, string name, MeshBuilder mb, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            mb.MinSubmeshCount = mats.Length;
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build(name);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        static void Kill(Transform t, string name)
        {
            for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i); if (c.name == name) Object.Destroy(c.gameObject); }
        }

        static void KO(MeshBuilder mb, Vector3 cIn, Quaternion rot)
        {
            // 1/2" + 3/4" concentric knockout: two punched seam rings and a pry tab
            mb.AddRing(V(cIn.x, cIn.y, cIn.z), rot, I(0.57f), I(0.53f), I(0.012f), 24, Seam);
            mb.AddRing(V(cIn.x, cIn.y, cIn.z), rot, I(0.455f), I(0.42f), I(0.012f), 20, Seam);
            mb.AddBox(V(cIn.x, cIn.y, cIn.z) + rot * new Vector3(I(0.49f), 0, 0), rot * new Vector3(I(0.1f), I(0.014f), I(0.05f)), Seam);
        }

        public static void Build(ElPanel p)
        {
            var s = p.Piece.Size / Units.Inch;
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, t = 0.07f;
            float F = FaceZ(p);
            float busTop = hy - BusTopFromTop, busBot = hy - 9.5f - 15f, busMid = (busTop + busBot) / 2, busLen = busTop - busBot;
            var mb = new MeshBuilder();
            var lq = new ElLabels.Quads();

            // ---------------- can
            mb.AddBox(V(0, 0, -hz + t / 2), V(s.x, s.y, t), Gray);
            mb.AddBox(V(-hx + t / 2, 0, 0), V(t, s.y, s.z), Gray);
            mb.AddBox(V(hx - t / 2, 0, 0), V(t, s.y, s.z), Gray);
            mb.AddBox(V(0, hy - t / 2, 0), V(s.x - 2 * t, t, s.z), Gray);
            mb.AddBox(V(0, -hy + t / 2, 0), V(s.x - 2 * t, t, s.z), Gray);
            // cover-screw tabs folded in at the front edge
            foreach (float y in new[] { 13f, 0f, -13f })
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    mb.AddBox(V(sg * (hx - 0.4f), y, hz - 0.05f), V(0.7f, 0.9f, 0.1f), Gray);
                    mb.AddCylinder(V(sg * (hx - 0.4f), y, hz - 0.1f), Quaternion.Euler(90, 0, 0), I(0.12f), I(0.02f), 10, Seam);
                }
            // knockouts (top/bottom/sides/back); used ones get connectors below
            foreach (float x in new[] { -6f, -2.5f, -1f, 0.5f, 4f, 5.6f }) KO(mb, new Vector3(x, hy + 0.006f, 0.2f), Quaternion.identity);
            foreach (float x in new[] { -5.6f, -4f, -2.5f, -1f, 0.5f, 4.2f, 5.6f }) if (x != -5.6f) KO(mb, new Vector3(x, -hy - 0.006f, 0.2f), Quaternion.identity);
            foreach (float y in new[] { 12f, 9f, -9f, -12f })
                for (int sg = -1; sg <= 1; sg += 2) KO(mb, new Vector3(sg * (hx + 0.006f), y, 0.2f), Quaternion.Euler(0, 0, 90));
            foreach (var c in new[] { new Vector2(-4.5f, 11f), new Vector2(4.5f, 11f), new Vector2(-4.5f, -11f), new Vector2(4.5f, -11f) })
                KO(mb, new Vector3(c.x, c.y, -hz + t + 0.006f), Quaternion.Euler(90, 0, 0));
            // keyhole mounting slots in the back
            foreach (var c in new[] { new Vector2(0, 13.2f), new Vector2(0, -13.2f) })
                mb.AddBox(V(c.x, c.y, -hz + t + 0.004f), V(0.28f, 0.9f, 0.01f), Dark);

            // wiring-diagram label on the inside of the left wall
            lq.Add(ElLabels.Text(new[] { "LOAD CENTER", "200A MAX  120/240V", "1 PHASE 3 WIRE", "USE COPPER OR AL", "WIRE 60/75 C", "TORQUE LUGS 250 LB-IN" }, 2.6f, 3.2f, Ink, false, 0, 80),
                V(-hx + t + 0.01f, -4f, -0.2f), Vector3.right, Vector3.up, I(2.6f), I(3.2f));
            mb.AddBox(V(-hx + t + 0.004f, -4f, -0.2f), V(0.006f, 3.4f, 2.8f), Card);

            // ---------------- interior: insulating base, bus legs, stabs, breaker rails
            mb.AddBevelBox(V(0, busMid, -hz + 0.45f), V(3.6f, busLen + 1f, 0.7f), Quaternion.identity, I(0.06f), Dark);
            for (int i = -1; i <= 1; i += 2) mb.AddBox(V(i * 0.3f, busMid + 0.5f, -1.05f), V(0.4f, busLen + 2f, 0.3f), Tin);
            for (int row = 0; row < ElPanel.Spaces / 2; row++)
            {
                float y = hy - 9.5f - row - 0.5f;
                float leg = row % 2 == 0 ? -0.3f : 0.3f; // phases alternate A/B down the bus
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float x0 = leg, x1 = sg * 0.95f;
                    mb.AddBox(V((x0 + x1) / 2, y, -0.86f), V(Mathf.Abs(x1 - x0) + 0.1f, 0.42f, 0.1f), Tin);
                    mb.AddBox(V(x1, y, -0.72f), V(0.08f, 0.42f, 0.3f), Tin);
                }
                for (int sg = -1; sg <= 1; sg += 2) mb.AddBox(V(sg * 3.1f, y + 0.3f, -0.95f), V(0.2f, 0.15f, 0.3f), Dark); // rail hook
            }
            for (int sg = -1; sg <= 1; sg += 2) mb.AddBox(V(sg * 3.1f, busMid, -hz + 0.5f), V(0.35f, busLen + 0.6f, 0.7f), Dark);

            // ---------------- main breaker, line lugs, load straps
            float my = hy - 5.6f;
            mb.AddBevelBox(V(0, my, F - 1.3f), V(4.4f, 4.2f, 2.6f), Quaternion.identity, I(0.08f), Dark);
            mb.AddBevelBox(V(0, my, F + 0.04f), V(2.6f, 3.2f, 0.12f), Quaternion.identity, I(0.04f), Dark);
            float mh = p.Main ? 0.6f : -0.6f;
            mb.AddBevelBox(V(0, my + mh, F + 0.32f), V(2.0f, 0.5f, 0.5f), Quaternion.identity, I(0.06f), Dark);
            lq.Add(ElLabels.Text(new[] { "200" }, 0.7f, 0.3f, new Color(0.95f, 0.95f, 0.9f), false, 0, 120), V(0, my + mh, F + 0.575f), Vector3.forward, Vector3.up, I(0.7f), I(0.3f));
            lq.Add(ElLabels.Text(new[] { "ON" }, 0.5f, 0.25f, new Color(0.95f, 0.95f, 0.9f)), V(-1.6f, my + 1.25f, F + 0.105f), Vector3.forward, Vector3.up, I(0.5f), I(0.25f));
            lq.Add(ElLabels.Text(new[] { "OFF" }, 0.6f, 0.25f, new Color(0.95f, 0.95f, 0.9f)), V(-1.6f, my - 1.25f, F + 0.105f), Vector3.forward, Vector3.up, I(0.6f), I(0.25f));
            for (int i = -1; i <= 1; i += 2)
            {
                mb.AddBox(V(i * 1.3f, hy - 2.6f, -0.6f), V(1.0f, 1.1f, 1.0f), Tin);                          // line lug
                mb.AddCylinder(V(i * 1.3f, hy - 2.02f, -0.6f), Quaternion.identity, I(0.2f), I(0.08f), 6, Galv); // hex set screw
                mb.AddBox(V(i * 1.3f, hy - 3.4f, -0.4f), V(0.5f, 0.8f, 0.15f), Tin);                           // strap into the main
                mb.AddBox(V(i * 0.3f, busTop + 0.4f, -0.95f), V(0.5f, 1.6f, 0.2f), Tin);                       // load strap to the bus
            }

            // ---------------- neutral bars (insulated), bonding screw, crossover; ground bar (bonded to the can)
            for (int sg = -1; sg <= 1; sg += 2)
            {
                foreach (float y in new[] { busTop - 0.2f, busBot + 0.2f }) mb.AddBox(V(sg * 5.3f, y, -hz + 0.35f), V(0.6f, 0.6f, 0.6f), Dark);
                mb.AddBox(V(sg * 5.3f, busMid, -1.15f), V(0.45f, busLen, 0.4f), Tin);
                for (float y = busBot + 0.4f; y < busTop; y += 0.5f)
                    mb.AddCylinder(V(sg * 5.3f, y, -0.93f), Quaternion.Euler(90, 0, 0), I(0.1f), I(0.08f), 8, Galv);
            }
            mb.AddCylinder(V(-5.3f, busBot + 0.6f, -0.9f), Quaternion.Euler(90, 0, 0), I(0.17f), I(0.12f), 6, Green);
            mb.AddBox(V(0, busBot - 0.95f, -1.3f), V(10.8f, 0.5f, 0.1f), Tin);
            for (int sg = -1; sg <= 1; sg += 2) mb.AddBox(V(sg * 5.3f, busBot - 0.5f, -1.3f), V(0.5f, 1.0f, 0.1f), Tin);
            float gy = -hy + 2.2f;
            mb.AddBox(V(0, gy, -hz + 0.3f), V(6.2f, 0.4f, 0.35f), Tin);
            for (float x = -2.75f; x <= 2.8f; x += 0.5f)
                mb.AddCylinder(V(x, gy, -hz + 0.5f), Quaternion.Euler(90, 0, 0), I(0.09f), I(0.07f), 8, Galv);
            for (int sg = -1; sg <= 1; sg += 2) mb.AddCylinder(V(sg * 3.3f, gy, -hz + 0.5f), Quaternion.Euler(90, 0, 0), I(0.15f), I(0.08f), 6, Green);

            // ---------------- branch breakers
            foreach (var b in p.Breakers)
            {
                Vector3 face = p.SpaceLocal(b.Slot) / Units.Inch;
                float sgn = face.x < 0 ? -1 : 1;
                float yc = b.Poles == 2 ? face.y - 0.5f : face.y;
                float hgt = b.Poles == 2 ? 1.96f : 0.98f;
                mb.AddBevelBox(V(sgn * 1.6f, yc, F - 1.35f), V(2.8f, hgt - 0.04f, 2.5f), Quaternion.identity, I(0.04f), Dark);
                mb.AddBox(V(sgn * 1.75f, yc, F - 0.05f), V(1.5f, hgt - 0.12f, 0.1f), Dark);
                float hxo = b.Tripped ? 0f : b.On ? -0.32f : 0.32f;
                var amp = ElLabels.Text(new[] { b.Amps.ToString() }, 0.3f, 0.16f, new Color(0.95f, 0.95f, 0.9f), false, 0, 160);
                for (int pole = 0; pole < b.Poles; pole++)
                {
                    float py = b.Poles == 2 ? face.y - pole * 1f : face.y;
                    mb.AddBevelBox(V(sgn * (1.75f + hxo), py, F + 0.17f), V(0.34f, 0.52f, 0.34f), Quaternion.identity, I(0.04f), Dark);
                    lq.Add(amp, V(sgn * (1.75f + hxo), py, F + 0.341f), Vector3.forward, Vector3.up, I(0.3f), I(0.16f));
                    mb.AddBox(V(sgn * 2.75f, py, F - 0.5f), V(0.42f, 0.42f, 0.42f), Tin);                                   // load lug
                    mb.AddCylinder(V(sgn * 2.75f, py, F - 0.25f), Quaternion.Euler(90, 0, 0), I(0.12f), I(0.06f), 10, Galv); // lug screw
                }
                if (b.Poles == 2) mb.AddBox(V(sgn * (1.75f + hxo), yc, F + 0.36f), V(0.14f, 1.0f, 0.06f), Galv); // handle tie
                if (b.Type != ElCatalog.BreakerType.Standard)
                    mb.AddBevelBox(V(sgn * 0.85f, yc, F + 0.04f), V(0.3f, 0.26f, 0.1f), Quaternion.identity, I(0.02f), b.Gfci ? Yellow : White);
            }
            var go = Spawn(p.transform, "Visual", mb, Mats);
            MepAutoAttach.ReplaceVisual(p.Piece, go);

            BuildWires(p, hx, hy, hz, F, busTop, busBot, gy);
            BuildConnectors(p, hy);
            Kill(p.transform, "Labels");
            Kill(p.transform, "DeadFront");
            Kill(p.transform, "CoverLabels");
            lq.Spawn(p.transform, "Labels");
            if (p.Cover) BuildCover(p, hx, hy, hz, F, busTop, busBot, my);
        }

        // ------------------------------------------------------------------ conductors
        static void BuildWires(ElPanel p, float hx, float hy, float hz, float F, float busTop, float busBot, float gy)
        {
            Kill(p.transform, "Wires");
            var mats = new List<Material> { ElVisuals.Black, ElVisuals.WhiteIns, ElVisuals.RedIns, ElVisuals.Copper, ElVisuals.Steel };
            var mm = new MepMesh { Origin = p.transform.position };
            Vector3 Wp(float x, float y, float z) => p.transform.TransformPoint(V(x, y, z));
            void Wire(List<Vector3> pts, float r, int sub) => mm.Sweep(MepMesh.RoundCorners(pts, I(0.6f), 5), MepMesh.Circle(I(r), 8), sub);
            int idx = 0, gIdx = 0;
            foreach (var b in p.Breakers)
            {
                if (b.Run == null) continue;
                var port = b.Run.PortAt(p.Node);
                if (port == null) continue;
                Vector3 e = port.LocalPos / Units.Inch;
                Vector3 face = p.SpaceLocal(b.Slot) / Units.Inch;
                float sgn = face.x < 0 ? -1 : 1;
                float gx = sgn * (3.9f + (idx % 4) * 0.12f), zc = -hz + 1.9f + (idx % 3) * 0.15f;
                bool top = port.LocalPos.y > 0;
                float dir = top ? -1f : 1f;
                float ex = e.x + (idx % 3 - 1) * 0.35f;
                // sheath runs ~1" past the connector, then the conductors fan out
                int sheath = mats.IndexOf(MepLook.Of(b.Run.Spec));
                if (sheath < 0) { mats.Add(MepLook.Of(b.Run.Spec)); sheath = mats.Count - 1; }
                mm.Sweep(new[] { Wp(e.x, e.y - dir * 0.3f, e.z), Wp(e.x, e.y, e.z), Wp(ex, e.y + dir * 1.0f, e.z), Wp(ex, e.y + dir * 1.4f, e.z) },
                    MepMesh.For(b.Run.Spec), sheath);
                float ey = e.y + dir * 1.4f;
                float ins = b.Run.Spec.Awg >= 14 ? 0.06f : 0.07f;
                Wire(new List<Vector3> { Wp(ex, ey, e.z), Wp(gx, ey + dir * 1f, zc), Wp(gx, face.y, zc), Wp(sgn * 2.75f, face.y, F - 0.5f) }, ins, 0);
                if (b.Run.Spec.Conductors >= 3 || b.Poles == 2)
                    Wire(new List<Vector3> { Wp(ex + 0.1f, ey, e.z), Wp(gx + 0.1f, ey + dir * 1f, zc + 0.1f), Wp(gx + 0.1f, face.y - 1f, zc + 0.1f), Wp(sgn * 2.75f, face.y - 1f, F - 0.5f) }, ins, b.Run.Spec.Conductors >= 3 ? 2 : 0);
                float by = Mathf.Clamp(face.y + 0.3f, busBot + 0.5f, busTop - 0.5f);
                Wire(new List<Vector3> { Wp(ex - 0.1f, ey, e.z), Wp(sgn * 4.6f, ey + dir * 1f, zc - 0.2f), Wp(sgn * 4.6f, by, zc - 0.2f), Wp(sgn * 5.3f, by, -0.93f) }, ins, 1);
                if (top)
                    Wire(new List<Vector3> { Wp(ex - 0.2f, ey, e.z), Wp(sgn * 4.75f, ey - 1f, zc - 0.35f), Wp(sgn * 4.75f, by - 0.75f, zc - 0.35f), Wp(sgn * 5.3f, by - 0.75f, -0.93f) }, 0.035f, 3);
                else
                {
                    // bottom-entry grounds land on the bonded ground bar
                    float gbx = Mathf.Clamp(e.x - 0.25f - gIdx * 0.5f, -2.75f, 2.75f);
                    Wire(new List<Vector3> { Wp(ex - 0.2f, ey, e.z), Wp(ex - 0.2f, ey + 0.4f, -0.6f), Wp(gbx, gy - 0.9f, -1.0f), Wp(gbx, gy, -hz + 0.5f) }, 0.035f, 3);
                    gIdx++;
                }
                if (b.Afci || b.Gfci) // coiled breaker pigtail neutral to the bar
                    Wire(new List<Vector3> { Wp(sgn * 2.9f, face.y - 0.2f, F - 1.8f), Wp(sgn * 3.6f, face.y - 0.6f, F - 1.3f), Wp(sgn * 4.2f, face.y - 0.2f, F - 1.6f), Wp(sgn * 5.3f, by - 0.4f, -0.93f) }, 0.06f, 1);
                idx++;
            }
            // service entrance: two hots to the line lugs, neutral to the left bar; grounding electrode conductor out the bottom
            var sp = p.ServicePort;
            if (sp != null && sp.Run != null)
            {
                Vector3 e = sp.LocalPos / Units.Inch;
                for (int i = -1; i <= 1; i += 2)
                    Wire(new List<Vector3> { Wp(e.x + i * 0.3f, e.y, e.z), Wp(e.x + i * 0.3f, e.y - 0.8f, e.z), Wp(i * 1.3f, hy - 1.0f, -0.6f), Wp(i * 1.3f, hy - 2.1f, -0.6f) }, 0.26f, 0);
                Wire(new List<Vector3> { Wp(e.x - 0.6f, e.y, e.z), Wp(e.x - 0.6f, e.y - 0.8f, e.z), Wp(-5.3f, hy - 3f, -0.9f), Wp(-5.3f, busTop, -0.9f) }, 0.2f, 4);
            }
            Wire(new List<Vector3> { Wp(-2.9f, gy, -hz + 0.5f), Wp(-4.6f, gy - 0.3f, -0.6f), Wp(-5.6f, -hy + 0.8f, 0.2f), Wp(-5.6f, -hy - 6f, 0.2f) }, 0.1f, 3);
            mm.Spawn(p.transform, "Wires", mats.ToArray());
        }

        // ------------------------------------------------------------------ NM cable connectors at used knockouts
        static void BuildConnectors(ElPanel p, float hy)
        {
            Kill(p.transform, "Connectors");
            var mb = new MeshBuilder();
            foreach (var port in p.Node.Ports)
            {
                if (port.Run == null && port.ExtraRuns.Count == 0) continue;
                Vector3 e = port.LocalPos / Units.Inch;
                float sg = e.y > 0 ? 1f : -1f;
                bool svc = port.Label == "service";
                float r = svc ? 0.8f : 0.5f;
                var rot = Quaternion.identity;
                mb.AddCylinder(V(e.x, sg * (hy + 0.25f), e.z), rot, I(r), I(0.5f), 16, 1);                    // hub
                mb.AddBox(V(e.x, sg * (hy + 0.62f), e.z), V(r * 2.3f, 0.2f, 0.45f), 1);                        // clamp saddle
                for (int k = -1; k <= 1; k += 2)
                    mb.AddCylinder(V(e.x + k * r * 0.95f, sg * (hy + 0.75f), e.z), rot, I(0.1f), I(0.14f), 8, 1); // clamp screws
                mb.AddRing(V(e.x, sg * (hy - 0.12f), e.z), rot, I(r + 0.2f), I(r - 0.05f), I(0.1f), 10, 1);   // locknut (inside)
            }
            // GEC connector at the bottom knockout
            {
                mb.AddCylinder(V(-5.6f, -hy - 0.2f, 0.2f), Quaternion.identity, I(0.35f), I(0.4f), 12, 1);
                mb.AddRing(V(-5.6f, -hy + 0.12f, 0.2f), Quaternion.identity, I(0.5f), I(0.3f), I(0.1f), 10, 1);
            }
            Spawn(p.transform, "Connectors", mb, Mats);
        }

        // ------------------------------------------------------------------ dead-front cover + door + directory
        static void BuildCover(ElPanel p, float hx, float hy, float hz, float F, float busTop, float busBot, float my)
        {
            var mb = new MeshBuilder();
            var lq = new ElLabels.Quads();
            float z0 = hz + 0.03f, th = 0.06f, zf = z0 + th / 2;
            float CX = 8f, CY = 16f;
            void Plate(float x0, float x1, float y0, float y1) { if (x1 - x0 > 0.001f && y1 - y0 > 0.001f) mb.AddBox(V((x0 + x1) / 2, (y0 + y1) / 2, z0), V(x1 - x0, y1 - y0, th), Gray); }
            const float wi = 0.95f, wo = 2.55f;          // breaker window from the center line
            float mw = 1.3f, m0 = my - 1.4f, m1 = my + 1.4f; // main window
            Plate(-CX, -wo, -CY, CY); Plate(wo, CX, -CY, CY);
            Plate(-wo, wo, m1, CY); Plate(-wo, wo, busTop, m0);
            Plate(-wo, -mw, m0, m1); Plate(mw, wo, m0, m1);
            Plate(-wo, wo, -CY, busBot);
            Plate(-wi, wi, busBot, busTop);
            for (int slot = 1; slot <= ElPanel.Spaces; slot++)
            {
                Vector3 f = p.SpaceLocal(slot) / Units.Inch;
                float sg = f.x < 0 ? -1 : 1;
                float x0 = sg < 0 ? -wo : wi, x1 = sg < 0 ? -wi : wo;
                if (p.BreakerAt(slot) != null) continue;
                // twist-out filler still in place: plate with the punched seam around it
                Plate(x0, x1, f.y - 0.5f, f.y + 0.5f);
                mb.AddBox(V((x0 + x1) / 2, f.y + 0.47f, zf + 0.002f), V(x1 - x0 - 0.12f, 0.025f, 0.004f), Seam);
                mb.AddBox(V((x0 + x1) / 2, f.y - 0.47f, zf + 0.002f), V(x1 - x0 - 0.12f, 0.025f, 0.004f), Seam);
                mb.AddBox(V(sg < 0 ? x1 - 0.06f : x0 + 0.06f, f.y, zf + 0.002f), V(0.025f, 0.9f, 0.004f), Seam);
            }
            // rolled return around the cover edge
            mb.AddBox(V(-CX + 0.03f, 0, z0 - 0.3f), V(0.06f, 2 * CY, 0.6f), Gray);
            mb.AddBox(V(CX - 0.03f, 0, z0 - 0.3f), V(0.06f, 2 * CY, 0.6f), Gray);
            mb.AddBox(V(0, CY - 0.03f, z0 - 0.3f), V(2 * CX, 0.06f, 0.6f), Gray);
            mb.AddBox(V(0, -CY + 0.03f, z0 - 0.3f), V(2 * CX, 0.06f, 0.6f), Gray);
            // cover screws into the can tabs
            foreach (float y in new[] { 13f, 0f, -13f })
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    mb.AddCylinder(V(sg * (hx - 0.4f), y, zf + 0.04f), Quaternion.Euler(90, 0, 0), I(0.2f), I(0.08f), 14, Tin);
                    mb.AddBox(V(sg * (hx - 0.4f), y, zf + 0.085f), V(0.3f, 0.05f, 0.01f), Seam);
                }
            // printed space numbers beside each window, nameplate, main label
            var odd = new string[15]; var even = new string[15];
            for (int r = 0; r < 15; r++) { odd[r] = (2 * r + 1).ToString(); even[r] = (2 * r + 2).ToString(); }
            float colY = hy - 9.5f - 7.5f;
            lq.Add(ElLabels.Text(odd, 0.5f, 15f, Ink, false, 15, 48, 2), V(wo + 0.35f, colY, zf + th / 2), Vector3.forward, Vector3.up, I(0.5f), I(15f));
            lq.Add(ElLabels.Text(even, 0.5f, 15f, Ink, false, 15, 48, 2), V(-wo - 0.35f, colY, zf + th / 2), Vector3.forward, Vector3.up, I(0.5f), I(15f));
            lq.Add(ElLabels.Text(new[] { "MAIN" }, 1.2f, 0.3f, Ink), V(0, m1 + 0.35f, zf + th / 2), Vector3.forward, Vector3.up, I(1.2f), I(0.3f));
            lq.Add(ElLabels.Text(new[] { "200A MAIN BREAKER LOAD CENTER", "120/240V  1 PH  3 W  30 SPACES" }, 7f, 0.9f, Ink, false, 0, 64), V(0, CY - 1.2f, zf + th / 2), Vector3.forward, Vector3.up, I(7f), I(0.9f));
            lq.Add(ElLabels.Text(new[] { "DANGER - HAZARD OF ELECTRIC SHOCK" }, 5f, 0.35f, new Color(0.8f, 0.1f, 0.08f), false, 0, 80), V(0, -CY + 1.0f, zf + th / 2), Vector3.forward, Vector3.up, I(5f), I(0.35f));

            // door, hinged on the left edge of the breaker area, swung open ~110 degrees
            float dx = 7.4f, d0 = busBot - 0.8f, d1 = m1 + 0.6f, dzc = zf + 0.42f;
            Vector3 hinge = new Vector3(-dx / 2, 0, dzc);
            var rot = Quaternion.Euler(0, -110f, 0);
            Vector3 D(float x, float y, float z) => V(hinge.x, 0, hinge.z) + rot * V(x, y, z); // door-local inches from the hinge line
            mb.Transform = Matrix4x4.TRS(V(hinge.x, 0, hinge.z), rot, Vector3.one);
            mb.AddBox(V(dx / 2, (d0 + d1) / 2, 0), V(dx, d1 - d0, 0.05f), Gray);
            mb.AddBox(V(dx - 0.03f, (d0 + d1) / 2, -0.2f), V(0.05f, d1 - d0, 0.4f), Gray);
            mb.AddBox(V(dx / 2, d1 - 0.03f, -0.2f), V(dx, 0.05f, 0.4f), Gray);
            mb.AddBox(V(dx / 2, d0 + 0.03f, -0.2f), V(dx, 0.05f, 0.4f), Gray);
            mb.AddBox(V(dx - 0.6f, (d0 + d1) / 2, 0.06f), V(0.5f, 1.4f, 0.08f), Dark);       // latch pull
            mb.AddBox(V(dx / 2, (d0 + d1) / 2 + 0.5f, -0.035f), V(6.2f, 13.5f, 0.01f), Card); // directory card
            mb.ResetTransform();
            foreach (float y in new[] { d0 + 1.5f, d1 - 1.5f })
                mb.AddCylinder(V(hinge.x, y, hinge.z), Quaternion.identity, I(0.12f), I(1.6f), 10, Gray); // hinge knuckles
            mb.AddBox(V(hinge.x + 0.15f, (d0 + d1) / 2, (zf + hinge.z) / 2), V(0.3f, d1 - d0 - 1f, hinge.z - zf), Gray);

            var lines = new List<string> { "CIRCUIT DIRECTORY", "", "MAIN 200A" };
            var sorted = new List<ElBreaker>(p.Breakers);
            sorted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var b in sorted)
            {
                string n = b.Slot + (b.Poles == 2 ? "/" + (b.Slot + 2) : "");
                string lab = string.IsNullOrEmpty(b.Label) ? (b.Run == null ? "SPARE" : "") : b.Label.ToUpperInvariant();
                string line = $"{n,-5} {lab}";
                if (line.Length > 24) line = line.Substring(0, 24);
                lines.Add($"{line,-24} {b.Amps}A");
            }
            var dirMat = ElLabels.Text(lines.ToArray(), 6f, 13f, Pen, true, 26, 60, 2);
            Vector3 n0 = rot * Vector3.back, up = Vector3.up;
            lq.Add(dirMat, D(dx / 2, (d0 + d1) / 2 + 0.5f, -0.045f), n0, up, I(6f), I(13f));

            Spawn(p.transform, "DeadFront", mb, Mats);
            lq.Spawn(p.transform, "CoverLabels");
        }
    }
}
