using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real-size procedural visuals for the electrical trade: blue PVC boxes, conductors + wire nuts inside a
    /// made-up box, devices (duplex / GFCI / switches), the open panel interior with breakers and landed conductors, cable coils.</summary>
    public static class ElVisuals
    {
        static float I(float x) => Units.In(x);

        public static Material Blue => Materials.Get(SurfaceKind.PaintedWhite, MepLook.BoxBlue);
        public static Material Steel => Materials.Get(SurfaceKind.Galvanized);
        public static Material Black => Materials.Get(SurfaceKind.DarkPlastic);
        public static Material WhiteIns => Materials.Get(SurfaceKind.PaintedWhite, new Color(1.04f, 1.04f, 1.02f));
        public static Material RedIns => Materials.Get(SurfaceKind.PaintedWhite, new Color(0.85f, 0.12f, 0.10f));
        public static Material Copper => Materials.Get(SurfaceKind.Brass, MepLook.Copper);
        public static Material GreenNut => Materials.Get(SurfaceKind.PaintedWhite, new Color(0.15f, 0.62f, 0.22f));
        public static Material YellowNut => Materials.Get(SurfaceKind.PaintedWhite, new Color(1.1f, 0.88f, 0.12f));
        public static Material RedNut => Materials.Get(SurfaceKind.PaintedWhite, new Color(0.92f, 0.16f, 0.12f));
        public static Material DeviceWhite => Materials.Get(SurfaceKind.PaintedWhite, new Color(1.03f, 1.02f, 0.99f));
        public static Material Brass => Materials.Get(SurfaceKind.Brass);
        public static Material Nickel => Materials.Get(SurfaceKind.Nickel);
        public static Material PanelGray => Materials.Get(SurfaceKind.PaintedSteel, new Color(0.72f, 0.73f, 0.75f));

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

        // ================================================================== box body

        public static void BuildBoxBody(ElBox b)
        {
            var mb = new MeshBuilder();
            float t = 0.11f;
            if (b.Kind == ElBoxKind.Round)
            {
                float r = ElBox.DRound * 0.5f, hy = ElBox.HRound * 0.5f;
                mb.AddRing(Vector3.zero, Quaternion.identity, I(r), I(r - t), I(ElBox.HRound), 32, 0);
                mb.AddDisc(new Vector3(0, I(hy), 0), Quaternion.identity, I(r), 32, 0, true);
                mb.AddDisc(new Vector3(0, I(hy - t), 0), Quaternion.identity, I(r - t), 32, 0, false);
                for (int s = -1; s <= 1; s += 2)
                    mb.AddBox(new Vector3(I(s * (r - 0.3f)), I(-hy + 0.2f), 0), new Vector3(I(0.45f), I(0.3f), I(0.6f)), 0); // device ears
                // nail-on side bracket against the joist (-X) + 2 nails
                mb.AddBox(new Vector3(-I(r + 0.06f), 0, 0), new Vector3(I(0.12f), I(ElBox.HRound), I(2.8f)), 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.AddRod(new Vector3(-I(r + 1.0f), I(0.3f), I(s * 0.9f)), Quaternion.LookRotation(Vector3.left), I(0.06f), I(2.2f), 8, 1);
                    mb.AddCylinder(new Vector3(-I(r - 0.05f), I(0.3f), I(s * 0.9f)), Quaternion.Euler(0, 0, 90), I(0.14f), I(0.06f), 10, 1);
                }
            }
            else
            {
                float W = b.Kind == ElBoxKind.TwoGang ? ElBox.W2g : ElBox.W1g, H = ElBox.H1g, D = ElBox.D1g;
                float hx = W / 2, hy = H / 2, hz = D / 2;
                mb.AddBox(new Vector3(0, 0, I(-hz + t / 2)), new Vector3(I(W), I(H), I(t)), 0);
                mb.AddBox(new Vector3(I(-hx + t / 2), 0, 0), new Vector3(I(t), I(H), I(D)), 0);
                mb.AddBox(new Vector3(I(hx - t / 2), 0, 0), new Vector3(I(t), I(H), I(D)), 0);
                mb.AddBox(new Vector3(0, I(hy - t / 2), 0), new Vector3(I(W - 2 * t), I(t), I(D)), 0);
                mb.AddBox(new Vector3(0, I(-hy + t / 2), 0), new Vector3(I(W - 2 * t), I(t), I(D)), 0);
                // device mounting ears (screw bosses) top and bottom inside the opening
                float[] gx = b.Kind == ElBoxKind.TwoGang ? new[] { -0.95f, 0.95f } : new[] { 0f };
                foreach (var x in gx)
                    for (int s = -1; s <= 1; s += 2)
                        mb.AddBox(new Vector3(I(x), I(s * (hy - t - 0.13f)), I(hz - 0.45f)), new Vector3(I(0.45f), I(0.26f), I(0.9f)), 0);
                // nail bosses inside the stud-side wall, captive nails driven through into the stud (-X)
                for (int s = -1; s <= 1; s += 2)
                {
                    float y = s * (hy - 0.65f), z = hz - 1.0f;
                    mb.AddBox(new Vector3(I(-hx + t + 0.15f), I(y), I(z)), new Vector3(I(0.3f), I(0.45f), I(0.6f)), 0);
                    mb.AddRod(new Vector3(I(-hx - 0.9f), I(y), I(z)), Quaternion.LookRotation(Vector3.left), I(0.06f), I(2.5f), 8, 1);
                    mb.AddCylinder(new Vector3(I(-hx + t + 0.32f), I(y), I(z)), Quaternion.Euler(0, 0, 90), I(0.14f), I(0.05f), 10, 1);
                }
                // depth-gauge ear resting on the stud face (box front 1/2" proud for drywall)
                mb.AddBox(new Vector3(I(-hx - 0.28f), 0, I(hz - 0.5f + 0.04f)), new Vector3(I(0.56f), I(0.9f), I(0.08f)), 0);
            }
            var go = Spawn(b.transform, "Visual", mb, new[] { Blue, Steel });
            MepAutoAttach.ReplaceVisual(b.Piece, go);
        }

        // ================================================================== box guts

        /// <summary>Box frame coordinates (inches) of a box-local point.</summary>
        static Vector3 Frame(ElBox b, Vector3 local)
        {
            Vector3 l = local / Units.Inch;
            return b.Kind == ElBoxKind.Round ? new Vector3(l.x, l.z, -l.y) : l;
        }

        public static void RebuildGuts(ElBox b)
        {
            Kill(b.transform, "Guts");
            Kill(b.transform, "Device");
            var cables = b.Cables;
            float fd = b.FrontDist / Units.Inch;
            bool round = b.Kind == ElBoxKind.Round;
            float devR = b.Kind == ElBoxKind.TwoGang ? -0.95f : 0f;
            bool dev = b.Device != ElDevice.None;
            bool sw = b.IsSwitch;
            int n = cables.Count;
            bool splice = n > 1;

            if (b.Stripped && n > 0)
            {
                var mm = new MepMesh { Origin = b.transform.position };
                // nut points (frame, inches)
                Vector3 gN, nN, hN;
                if (round) { gN = new Vector3(-0.8f, 0.2f, fd - 0.5f); nN = new Vector3(0.1f, 0.8f, fd - 0.4f); hN = new Vector3(0.6f, -0.6f, fd - 0.4f); }
                else
                {
                    // splices sit just below the knockouts the cables come in through (top or bottom)
                    float up = 0f;
                    foreach (var r in cables) { var pp = r.PortAt(b.Node); if (pp != null) up += pp.LocalPos.y > 0 ? 1 : -1; }
                    float su = up >= 0 ? 1f : -1f;
                    gN = new Vector3(devR - 0.1f, su * 1.05f, -0.75f);
                    nN = new Vector3(devR - 0.5f, su * 0.2f, -0.1f);
                    hN = new Vector3(devR + 0.5f, su * 0.2f, -0.1f);
                }
                // device terminals
                Vector3 tG = new Vector3(devR - 0.3f, -1.55f, fd - 0.35f);
                Vector3 tN = sw ? new Vector3(devR + 0.68f, -0.5f, fd - 0.7f) : new Vector3(devR - 0.68f, 0.5f, fd - 0.7f);
                Vector3 tH = new Vector3(devR + 0.68f, 0.5f, fd - 0.7f);
                if (sw) tN = tH + new Vector3(0, -1.0f, 0); // switches have no neutral: second brass screw for the switch leg

                int ci = 0;
                foreach (var r in cables)
                {
                    var port = r.PortAt(b.Node);
                    if (port == null) continue;
                    Vector3 P = Frame(b, port.LocalPos);
                    Vector3 pd = Frame(b, port.LocalDir * Units.Inch).normalized;
                    Vector3 inward = -pd;
                    Vector3 side = Vector3.right;
                    int awg = r.Spec.Awg;
                    float rIns = awg >= 14 ? 0.06f : awg >= 12 ? 0.068f : 0.085f, rBare = awg >= 14 ? 0.032f : awg >= 12 ? 0.04f : 0.05f;
                    var kinds = new List<int> { 0, 1 }; if (r.Spec.Conductors >= 3) kinds.Add(2); kinds.Add(3); // 0 black 1 white 2 red 3 bare
                    for (int k = 0; k < kinds.Count; k++)
                    {
                        int kind = kinds[k];
                        float off = (k - (kinds.Count - 1) * 0.5f) * 0.12f;
                        Vector3 s0 = P - pd * 0.05f + side * off;
                        Vector3 s1 = s0 + inward * 0.55f;
                        bool done = kind == 3 ? b.Grounds : kind == 1 ? b.Neutrals : b.Hots;
                        Vector3 nut = kind == 3 ? gN : kind == 1 ? nN : hN;
                        Vector3 term = kind == 3 ? tG : kind == 1 ? tN : tH;
                        var path = new List<Vector3> { s0, s1 };
                        if (done && splice) { path.Add(Vector3.Lerp(s1, nut, 0.6f) + side * off * 0.5f); path.Add(nut + side * off * 0.4f); }
                        else if (done && dev) { path.Add((s1 + term) * 0.5f + new Vector3(0, 0, 0.3f)); path.Add(term); }
                        else
                        {
                            // loose ends folded out of the box opening (~3")
                            float sgn = Vector3.Dot(pd, round ? new Vector3(0, 0, -1) : Vector3.up) > 0 ? 1f : -1f;
                            Vector3 lip = new Vector3(devR + off * 2.2f + (ci - n * 0.5f) * 0.25f, sgn * (round ? 0.4f : 1.2f), fd - 0.3f);
                            path.Add(lip);
                            path.Add(lip + new Vector3(off * 2f, sgn * 0.6f, 2.6f));
                        }
                        var world = new List<Vector3>();
                        foreach (var q in path) world.Add(b.W(q.x, q.y, q.z));
                        var smooth = MepMesh.RoundCorners(world, I(0.5f), 5);
                        mm.Sweep(smooth, MepMesh.Circle(I(kind == 3 ? rBare : rIns), 8), kind);
                    }
                    ci++;
                }
                // wire nuts + pigtails
                if (splice)
                {
                    void Nut(Vector3 at, int sub, bool spliced, int pigKind, Vector3 term)
                    {
                        if (!spliced) return;
                        Vector3 a = b.W(at.x, at.y, at.z), dir = b.transform.TransformDirection(b.F);
                        mm.Band(a - dir * I(0.05f), a + dir * I(0.45f), MepMesh.Circle(I(0.22f), 12), sub);
                        mm.Band(a + dir * I(0.45f), a + dir * I(0.75f), MepMesh.Circle(I(0.15f), 12), sub);
                        // pigtail to the device terminal (or folded out the front for the trim-out)
                        Vector3 end = dev ? term : new Vector3(at.x, at.y, fd + 2.2f);
                        var pts = new List<Vector3> { b.W(at.x + 0.1f, at.y + 0.12f, at.z - 0.1f), b.W(at.x + 0.25f, at.y + 0.3f, at.z + 0.6f), b.W(end.x, end.y, end.z - (dev ? 0.4f : 0f)), b.W(end.x, end.y, end.z) };
                        mm.Sweep(MepMesh.RoundCorners(pts, I(0.4f), 5), MepMesh.Circle(I(pigKind == 3 ? 0.035f : 0.062f), 8), pigKind);
                    }
                    Nut(gN, 4, b.Grounds, 3, tG);
                    Nut(nN, 5, b.Neutrals && !(sw), 1, tN);
                    Nut(hN, 6, b.Hots, 0, tH);
                    if (sw && b.Neutrals)
                    {
                        // neutrals spliced and folded to the back in a switch box
                        Vector3 a = b.W(nN.x, nN.y, nN.z), dir = b.transform.TransformDirection(b.F);
                        mm.Band(a - dir * I(0.05f), a + dir * I(0.45f), MepMesh.Circle(I(0.22f), 12), 5);
                        mm.Band(a + dir * I(0.45f), a + dir * I(0.75f), MepMesh.Circle(I(0.15f), 12), 5);
                    }
                }
                mm.Spawn(b.transform, "Guts", new[] { Black, WhiteIns, RedIns, Copper, GreenNut, YellowNut, RedNut });
            }
            if (dev && !round) BuildDevice(b, fd, devR);
        }

        // ================================================================== devices

        static void BuildDevice(ElBox b, float fd, float devR)
        {
            var mb = new MeshBuilder();
            var m = Matrix4x4.TRS(b.F * I(fd) + b.R * I(devR), Quaternion.LookRotation(b.F, b.U), Vector3.one);
            mb.Transform = m;
            const int W = 0, S = 1, D = 2, B = 3, N = 4, G = 5;
            var face = Quaternion.Euler(90, 0, 0);
            // yoke strap + ears
            mb.AddBox(new Vector3(0, 0, -I(0.02f)), new Vector3(I(0.85f), I(3.6f), I(0.04f)), S);
            for (int s = -1; s <= 1; s += 2) mb.AddBox(new Vector3(0, s * I(1.9f), -I(0.02f)), new Vector3(I(1.3f), I(0.55f), I(0.04f)), S);
            // body behind the yoke
            mb.AddBox(new Vector3(0, 0, -I(0.65f)), new Vector3(I(1.3f), I(2.6f), I(1.2f)), W);
            switch (b.Device)
            {
                case ElDevice.Switch:
                case ElDevice.Switch3:
                {
                    mb.AddBevelBox(new Vector3(0, 0, I(0.06f)), new Vector3(I(1.3f), I(2.6f), I(0.12f)), Quaternion.identity, I(0.03f), W);
                    mb.AddBevelBox(new Vector3(0, 0, I(0.16f)), new Vector3(I(0.42f), I(0.95f), I(0.1f)), Quaternion.identity, I(0.03f), W);
                    float tilt = b.SwitchOn ? -22f : 22f;
                    mb.AddBevelBox(new Vector3(0, (b.SwitchOn ? 1 : -1) * I(0.2f), I(0.36f)), new Vector3(I(0.27f), I(0.55f), I(0.17f)), Quaternion.Euler(tilt, 0, 0), I(0.05f), W);
                    for (int s = -1; s <= 1; s += 2) mb.AddCylinder(new Vector3(I(0.68f), s * I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, B);
                    if (b.Device == ElDevice.Switch3) mb.AddCylinder(new Vector3(-I(0.68f), I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, D);
                    break;
                }
                case ElDevice.Gfci:
                {
                    mb.AddBevelBox(new Vector3(0, 0, I(0.12f)), new Vector3(I(1.3f), I(2.6f), I(0.24f)), Quaternion.identity, I(0.04f), W);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float y = s * I(0.85f);
                        mb.AddBevelBox(new Vector3(0, y, I(0.27f)), new Vector3(I(1.05f), I(0.7f), I(0.08f)), Quaternion.identity, I(0.06f), W);
                        Slots(mb, y, I(0.315f), true, D);
                    }
                    mb.AddBevelBox(new Vector3(0, I(0.13f), I(0.28f)), new Vector3(I(0.55f), I(0.2f), I(0.08f)), Quaternion.identity, I(0.02f), D);
                    mb.AddBevelBox(new Vector3(0, -I(0.13f), I(0.28f)), new Vector3(I(0.55f), I(0.2f), I(0.08f)), Quaternion.identity, I(0.02f), W);
                    break;
                }
                default:
                {
                    mb.AddBevelBox(new Vector3(0, 0, I(0.07f)), new Vector3(I(1.32f), I(2.62f), I(0.14f)), Quaternion.identity, I(0.03f), W);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float y = s * I(0.66f);
                        mb.AddBevelBox(new Vector3(0, y, I(0.17f)), new Vector3(I(1.12f), I(1.0f), I(0.08f)), Quaternion.identity, I(0.08f), W);
                        Slots(mb, y, I(0.215f), b.Device == ElDevice.Receptacle20, D);
                    }
                    mb.AddDisc(new Vector3(0, 0, I(0.145f)), face, I(0.09f), 12, N, true);
                    mb.AddCylinder(new Vector3(I(0.68f), I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, B);
                    mb.AddCylinder(new Vector3(I(0.68f), -I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, B);
                    mb.AddCylinder(new Vector3(-I(0.68f), I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, N);
                    mb.AddCylinder(new Vector3(-I(0.68f), -I(0.5f), -I(0.7f)), Quaternion.Euler(0, 0, 90), I(0.13f), I(0.1f), 10, N);
                    break;
                }
            }
            // green ground screw on the yoke
            mb.AddCylinder(new Vector3(-I(0.3f), -I(1.55f), I(0.02f)), face, I(0.1f), I(0.08f), 10, G);
            Spawn(b.transform, "Device", mb, new[] { DeviceWhite, Steel, Black, Brass, Nickel, GreenNut });
        }

        static void Slots(MeshBuilder mb, float y, float z, bool tSlot, int dark)
        {
            mb.AddBox(new Vector3(I(0.25f), y + I(0.12f), z), new Vector3(I(0.07f), I(0.28f), I(0.012f)), dark);   // hot (short)
            mb.AddBox(new Vector3(-I(0.25f), y + I(0.12f), z), new Vector3(I(0.07f), I(0.36f), I(0.012f)), dark);  // neutral (long)
            if (tSlot) mb.AddBox(new Vector3(-I(0.31f), y + I(0.05f), z), new Vector3(I(0.16f), I(0.06f), I(0.012f)), dark);
            mb.AddDisc(new Vector3(0, y - I(0.25f), z), Quaternion.Euler(90, 0, 0), I(0.09f), 12, dark, true);       // ground
        }

        // ================================================================== panel

        static readonly Dictionary<ElPanel, int> _panelHash = new Dictionary<ElPanel, int>();

        public static void UpdatePanel(ElPanel p)
        {
            int h = p.Version * 31 + (p.Main ? 7 : 3) + NetGraph.Version * 131;
            foreach (var b in p.Breakers) h = h * 17 + b.Slot * 5 + (b.On ? 1 : 0) + (b.Tripped ? 2 : 0) + (b.Run != null ? 4 : 0);
            if (_panelHash.TryGetValue(p, out int old) && old == h) return;
            _panelHash[p] = h;
            BuildPanel(p);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _panelHash.Clear();

        static void BuildPanel(ElPanel p)
        {
            var s = p.Piece.Size / Units.Inch;
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, t = 0.07f;
            var mb = new MeshBuilder();
            const int Gray = 0, Galv = 1, Dark = 2, Tin = 3, White = 4, Red = 5;
            Vector3 V(float x, float y, float z) => new Vector3(I(x), I(y), I(z));
            // can (dead-front off for rough-in)
            mb.AddBox(V(0, 0, -hz + t / 2), V(s.x, s.y, t), Gray);
            mb.AddBox(V(-hx + t / 2, 0, 0), V(t, s.y, s.z), Gray);
            mb.AddBox(V(hx - t / 2, 0, 0), V(t, s.y, s.z), Gray);
            mb.AddBox(V(0, hy - t / 2, 0), V(s.x - 2 * t, t, s.z), Gray);
            mb.AddBox(V(0, -hy + t / 2, 0), V(s.x - 2 * t, t, s.z), Gray);
            mb.AddBox(V(0, 0, -hz + t + 0.01f), V(s.x - 2 * t, s.y - 2 * t, 0.02f), White);
            // interior: insulating base + bus stabs
            float busTop = hy - 9f, busBot = hy - 9.5f - 15f;
            mb.AddBox(V(0, (busTop + busBot) / 2, -hz + 0.45f), V(3.4f, busTop - busBot, 0.7f), Dark);
            for (int i = -1; i <= 1; i += 2) mb.AddBox(V(i * 0.45f, (busTop + busBot) / 2, -hz + 0.95f), V(0.35f, busTop - busBot, 0.3f), Tin);
            // main lugs + 200 A main breaker
            for (int i = -1; i <= 1; i += 2) mb.AddBox(V(i * 1.3f, hy - 2.6f, -hz + 1.2f), V(0.9f, 1.0f, 1.0f), Tin);
            mb.AddBox(V(0, hy - 5.6f, -hz + 1.7f), V(4.4f, 4.2f, 2.6f), Dark);
            mb.AddBox(V(0, hy - 5.6f + (p.Main ? 0.7f : -0.7f), -hz + 3.15f), V(1.8f, 0.55f, 0.55f), Dark);
            mb.AddBox(V(0, hy - 5.6f, -hz + 3.02f), V(0.9f, 0.35f, 0.02f), p.Main ? Red : White);
            // neutral / ground bars
            for (int i = -1; i <= 1; i += 2)
            {
                mb.AddBox(V(i * 5.3f, (busTop + busBot) / 2, -hz + 0.8f), V(0.42f, busTop - busBot, 0.42f), Tin);
                for (float y = busBot + 0.4f; y < busTop; y += 0.75f)
                    mb.AddCylinder(V(i * 5.3f, y, -hz + 1.06f), Quaternion.Euler(90, 0, 0), I(0.09f), I(0.1f), 8, Tin);
            }
            // breakers
            foreach (var b in p.Breakers)
            {
                Vector3 face = p.SpaceLocal(b.Slot) / Units.Inch;
                float sgn = face.x < 0 ? -1 : 1;
                float yc = face.y - (b.Poles == 2 ? 1f : 0f) * 0.5f * 2f / 2f * 1f;
                if (b.Poles == 2) yc = face.y - 0.5f;
                float hgt = b.Poles == 2 ? 1.95f : 0.97f;
                mb.AddBox(V(sgn * 1.55f, yc, face.z - 1.25f), V(2.7f, hgt, 2.5f), Dark);
                float hxo = b.Tripped ? 0f : b.On ? -0.35f : 0.35f;
                mb.AddBox(V(sgn * (1.55f + hxo), yc, face.z + 0.2f), V(0.42f, hgt * 0.55f, 0.4f), Dark);
                if (b.Type != ElCatalog.BreakerType.Standard)
                    mb.AddBox(V(sgn * 0.75f, yc, face.z + 0.02f), V(0.3f, 0.25f, 0.06f), b.Type == ElCatalog.BreakerType.Gfci ? Tin : White);
            }
            var go = Spawn(p.transform, "Visual", mb, new[] { PanelGray, Steel, Black, Nickel, DeviceWhite, RedNut });
            MepAutoAttach.ReplaceVisual(p.Piece, go);

            // conductors (world sweeps)
            Kill(p.transform, "Wires");
            var mm = new MepMesh { Origin = p.transform.position };
            Vector3 Wp(float x, float y, float z) => p.transform.TransformPoint(V(x, y, z));
            void Wire(List<Vector3> pts, float r, int sub) => mm.Sweep(MepMesh.RoundCorners(pts, I(0.6f), 5), MepMesh.Circle(I(r), 8), sub);
            int idx = 0;
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
                float ey = e.y + (top ? -0.4f : 0.4f);
                float ins = b.Run.Spec.Awg >= 14 ? 0.06f : 0.07f;
                // black to the breaker terminal
                Wire(new List<Vector3> { Wp(e.x, e.y, e.z), Wp(e.x, ey, e.z), Wp(gx, ey + (top ? -1f : 1f), zc), Wp(gx, face.y, zc), Wp(sgn * 2.8f, face.y, face.z - 0.7f) }, ins, 0);
                if (b.Run.Spec.Conductors >= 3 || b.Poles == 2)
                    Wire(new List<Vector3> { Wp(e.x + 0.1f, e.y, e.z), Wp(e.x + 0.1f, ey, e.z), Wp(gx + 0.1f, ey + (top ? -1f : 1f), zc + 0.1f), Wp(gx + 0.1f, face.y - 1f, zc + 0.1f), Wp(sgn * 2.8f, face.y - 1f, face.z - 0.7f) }, ins, 2);
                // white + bare to the neutral/ground bar
                float by = Mathf.Clamp(face.y + 0.3f, busBot + 0.5f, busTop - 0.5f);
                Wire(new List<Vector3> { Wp(e.x - 0.1f, e.y, e.z), Wp(e.x - 0.1f, ey, e.z), Wp(sgn * 4.6f, ey + (top ? -1f : 1f), zc - 0.2f), Wp(sgn * 4.6f, by, zc - 0.2f), Wp(sgn * 5.3f, by, -hz + 1.06f) }, ins, 1);
                Wire(new List<Vector3> { Wp(e.x - 0.2f, e.y, e.z), Wp(e.x - 0.2f, ey, e.z), Wp(sgn * 4.75f, ey + (top ? -1f : 1f), zc - 0.35f), Wp(sgn * 4.75f, by - 0.75f, zc - 0.35f), Wp(sgn * 5.3f, by - 0.75f, -hz + 1.06f) }, 0.035f, 3);
                if (b.Afci || b.Gfci) // breaker pigtail neutral to the bar
                    Wire(new List<Vector3> { Wp(sgn * 2.9f, face.y - 0.2f, face.z - 1.8f), Wp(sgn * 4.2f, face.y - 0.2f, face.z - 1.6f), Wp(sgn * 5.3f, by - 0.4f, -hz + 1.06f) }, 0.06f, 1);
                idx++;
            }
            // service conductors: SER to main lugs / neutral bar
            var sp = p.ServicePort;
            if (sp != null && sp.Run != null)
            {
                Vector3 e = sp.LocalPos / Units.Inch;
                for (int i = -1; i <= 1; i += 2)
                    Wire(new List<Vector3> { Wp(e.x + i * 0.3f, e.y, e.z), Wp(e.x + i * 0.3f, e.y - 0.8f, e.z), Wp(i * 1.3f, hy - 1.6f, -hz + 1.2f), Wp(i * 1.3f, hy - 2.2f, -hz + 1.2f) }, 0.26f, 0);
                Wire(new List<Vector3> { Wp(e.x - 0.6f, e.y, e.z), Wp(e.x - 0.6f, e.y - 0.8f, e.z), Wp(-5.3f, hy - 3f, -hz + 1.0f), Wp(-5.3f, busTop, -hz + 1.0f) }, 0.2f, 4);
            }
            mm.Spawn(p.transform, "Wires", new[] { Black, WhiteIns, RedIns, Copper, Steel });
        }

        // ================================================================== cable coil

        public static void AttachCoil(Piece p)
        {
            RunSpec spec = null;
            foreach (var s in ElCatalog.Specs()) if (s.StockId == p.Def.Id) spec = s;
            if (spec == null) return;
            var mb = new MeshBuilder();
            float r = p.Size.x * 0.5f, h = p.Size.y;
            if (spec.Id == ElCatalog.SerId) { mb.AddBox(Vector3.zero, p.Size, 0); }
            else
            {
                mb.AddRing(Vector3.zero, Quaternion.identity, r, r - Units.In(3.5f), h, 32, 0);
                mb.AddRing(new Vector3(0, 0, 0), Quaternion.identity, r + 0.002f, r - Units.In(3.5f) - 0.002f, h * 0.35f, 32, 1); // shrink-wrap label band
            }
            var go = Spawn(p.transform, "Visual", mb, new[] { MepLook.Of(spec), Materials.Get(SurfaceKind.PaintedWhite, new Color(0.25f, 0.35f, 0.75f)) });
            MepAutoAttach.ReplaceVisual(p, go);
        }
    }
}
