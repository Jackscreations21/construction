using UnityEngine;
using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // =====================================================================================================================
    /// <summary>Rotted 5/4x6 deck board. Frame: centre of the board's top face, +Z along the board.</summary>
    public class DeckBoardDefect : RepairDefect
    {
        float L => House != null && House.DeckBoardLength > 0.5f ? House.DeckBoardLength : 3.66f;
        int Joists => Mathf.FloorToInt((L - 0.04f) / Units.In(16)) + 1;
        const float W = 0.1397f, T = 0.0254f;

        public override string Title => "Rotted deck board";
        public override string Symptom => "One of the deck boards is soft. My foot nearly went through it.";
        public override string Observation => Has("fastened") ? "New board is down, gapped and screwed at every joist."
            : Has("set") ? "New board is in the gap, not fastened yet."
            : Has("removed") ? "The board is out. Joists are exposed" + (Has("cut") ? "; the new board is cut and lying on the deck." : ".")
            : "One grey, cupped board with a dark, crumbling section about two feet long.";
        public override bool IsFixed => Has("fastened");
        protected override float SkipPenalty => Has("joistOk") ? 0f : 0.1f;
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0, 0), new Vector3(0.5f, 0.25f, L));
        protected override void Restore() { House.SetFixtureVisible(Spot, true); }

        protected override void DefineActions()
        {
            Act("probe", "Probe the board with a screwdriver", C.Screwdriver, 1.5f, () => !Has("removed"), () => "The screwdriver sinks half an inch into the dark section. The rest of the board is hard. Rot, not just weathering.").Diag();
            Act("unscrew", "Back out the deck screws at every joist", C.Drill, 4f, () => !Has("unscrewed") && !Has("removed"), () => { Set("unscrewed"); return $"{Joists * 2} screws out."; }).Snd("drill");
            Act("pry", "Pry the board up and out", C.PryBar, 3f, () => !Has("removed"), () =>
            {
                Set("removed"); Dirty(0.15f);
                if (!Has("unscrewed")) { Lose(0.1f); return "It comes up, tearing the screws through and chewing the edges of the boards either side."; }
                return "Board is out in one piece.";
            }).Snd("knock");
            Act("joist", "Check the joist tops for rot where the bad section sat", C.Screwdriver, 2f, () => Has("removed") && !Has("joistOk"), () => { Set("joistOk"); return "Joists are solid: the screwdriver won't dent them."; }).Diag();
            Act("cut", $"Measure the gap and cut a new 5/4x6 PT board to {Units.Format(L)}", C.Saw, 3f, () => !Has("cut"), () => { Set("cut"); return $"New board cut to {Units.Format(L)}."; }).Uses(C.DeckBoardFor(L)).Snd("saw");
            Act("set", "Lay the new board in, crown up, with a 1/8\" gap each side (16d nail spacers)", null, 2f, () => Has("removed") && Has("cut") && !Has("set"), () => { Set("set"); return "Board is in with even gaps."; }).Snd("thud");
            Act("fasten", "Pre-drill the ends, then two deck screws into every joist", C.Drill, 5f, () => Has("set") && !Has("fastened"), () => { Set("fastened"); S.mess = 0f; return $"{Joists * 2} screws, heads just below the surface. Old board is in the van."; })
                .Uses(C.DeckScrews, Joists * 2).Snd("drill");
        }

        protected override void BuildModel(RM m)
        {
            House.SetFixtureVisible(Spot, false);
            float l = L;
            int screw = m.Mat(new Color(0.2f, 0.19f, 0.17f));
            if (!Has("removed"))
            {
                int old = m.Mat(SurfaceKind.TreatedLumber, new Color(0.72f, 0.7f, 0.66f)), rot = m.Mat(SurfaceKind.TreatedLumber, new Color(0.3f, 0.24f, 0.18f));
                float a = -l * 0.2f, b = a + 0.6f;
                m.Box(new Vector3(0, -T / 2, (-l / 2 + a) / 2), new Vector3(W, T, a + l / 2), Quaternion.identity, old, Vector3.forward);
                m.Box(new Vector3(0, -T / 2, (b + l / 2) / 2), new Vector3(W, T, l / 2 - b), Quaternion.identity, old, Vector3.forward);
                m.Box(new Vector3(0, -T / 2 - 0.005f, (a + b) / 2), new Vector3(W, T - 0.01f, b - a), Quaternion.identity, rot, Vector3.forward);
                int hole = m.Mat(new Color(0.07f, 0.05f, 0.04f));
                for (int i = 0; i < 5; i++) m.Box(new Vector3(-0.04f + 0.02f * i, -0.0095f, a + 0.08f + i * 0.1f), new Vector3(0.012f + 0.006f * (i % 2), 0.001f, 0.09f), Quaternion.Euler(0, i * 9f - 15f, 0), hole);
                if (!Has("unscrewed")) Screws(m, screw, l);
            }
            else if (Has("set"))
            {
                m.Box(new Vector3(0, -T / 2, 0), new Vector3(W, T, l), Quaternion.identity, m.Mat(SurfaceKind.TreatedLumber), Vector3.forward);
                if (Has("fastened")) Screws(m, screw, l);
            }
            if (Has("cut") && !Has("set"))
                m.Box(new Vector3(0.45f, T / 2, 0.1f), new Vector3(W, T, l), Quaternion.Euler(0, 4f, 0), m.Mat(SurfaceKind.TreatedLumber), Vector3.forward);
        }

        void Screws(RM m, int mat, float l)
        {
            for (float z = -l / 2 + 0.02f + Units.In(0.75f); z <= l / 2; z += Units.In(16))
                foreach (float sx in new[] { -0.04f, 0.04f }) m.Disc(new Vector3(sx, 0.0006f, z), 0.004f, mat, 8);
        }
    }

    // =====================================================================================================================
    /// <summary>Broken cedar fence picket. Frame: base centre of the picket, +Y up, pickets face ±Z.</summary>
    public class FencePicketDefect : RepairDefect
    {
        const float W = 0.1397f, T = 0.0159f, H = 1.829f;
        public override string Title => "Broken fence picket";
        public override string Symptom => "A picket in the back fence snapped off. The dog keeps getting out through the gap.";
        public override string Observation => Has("fastened") ? "New picket, tops in line with its neighbours, two screws in each rail."
            : Has("set") ? "New picket is held in place, not fastened." : Has("removed") ? "Gap in the fence. " + (Has("nails") ? "Rails are clean." : "Old nails still stick out of the rails.")
            : "The picket is snapped at the middle rail and black with rot at the bottom. The top half is on the ground.";
        public override bool IsFixed => Has("fastened");
        public override float BookHours => 0.5f;
        protected override float SkipPenalty => Has("nails") ? 0f : 0.1f;
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0.9f, 0), new Vector3(0.34f, 1.9f, 0.5f));

        protected override void DefineActions()
        {
            Act("pry", "Pry the broken picket off the rails", C.PryBar, 2f, () => !Has("removed"), () => { Set("removed"); Dirty(0.15f); return "Old picket is off."; }).Snd("knock");
            Act("nails", "Pull the old nails out of the rails", C.Hammer, 2f, () => Has("removed") && !Has("nails") && !Has("set"), () => { Set("nails"); return "Rails are clean."; }).Snd("hammer");
            Act("set", "Hold a new dog-ear picket 2\" off the ground, top level with its neighbours, same gap each side", C.Level, 2f, () => Has("removed") && !Has("set"), () => { Set("set"); return "Picket is plumb and in line."; }).Uses(C.Picket);
            Act("fasten", "Two exterior screws into each of the three rails", C.Drill, 3f, () => Has("set") && !Has("fastened"), () => { Set("fastened"); S.mess = 0f; return "Six screws. The broken pieces are in the van."; }).Uses(C.DeckScrews, 6).Snd("drill");
        }

        protected override void BuildModel(RM m)
        {
            House.SetFixtureVisible(Spot, false);
            if (!Has("removed"))
            {
                int old = m.Mat(SurfaceKind.PineLumber, new Color(0.6f, 0.55f, 0.5f)), rot = m.Mat(SurfaceKind.PineLumber, new Color(0.22f, 0.18f, 0.15f));
                var lean = Quaternion.Euler(6f, 0, 3f);
                m.Box(lean * new Vector3(0, 0.55f, 0), new Vector3(W, 0.7f, T), lean, old, Vector3.up);
                m.Box(lean * new Vector3(0, 0.1f, 0), new Vector3(W, 0.2f, T + 0.002f), lean, rot, Vector3.up);
                m.Box(lean * new Vector3(0.02f, 0.92f, 0), new Vector3(W * 0.6f, 0.06f, T), lean * Quaternion.Euler(0, 0, 25f), old, Vector3.up);   // splintered end
                m.Box(new Vector3(0.25f, T / 2, 0.45f), new Vector3(W, T, 0.9f), Quaternion.Euler(0, 35f, 0), old, Vector3.forward);
            }
            else if (Has("set"))
            {
                int cedar = m.Mat(SurfaceKind.PineLumber, new Color(1f, 0.8f, 0.62f));
                m.Box(new Vector3(0, (H - 0.04f) / 2 + 0.02f, 0), new Vector3(W, H - 0.04f, T), Quaternion.identity, cedar, Vector3.up);
                m.Box(new Vector3(0, H - 0.0f, 0), new Vector3(W - 0.05f, 0.04f, T), Quaternion.identity, cedar, Vector3.up);          // dog-ear top
                if (Has("fastened"))
                {
                    int screw = m.Mat(new Color(0.2f, 0.19f, 0.17f));
                    foreach (float y in new[] { 0.25f, 0.9f, 1.55f }) foreach (float sx in new[] { -0.035f, 0.035f }) foreach (float sz in new[] { -1f, 1f })
                        m.Box(new Vector3(sx, y, sz * (T / 2 + 0.0005f)), new Vector3(0.007f, 0.007f, 0.001f), screw);
                }
            }
            else if (!Has("nails"))
            {
                int nail = m.Mat(SurfaceKind.Galvanized);
                foreach (float y in new[] { 0.25f, 0.9f, 1.55f }) m.Tube(new Vector3(0.02f, y, -0.03f), new Vector3(0.02f, y + 0.01f, 0.03f), 0.0015f, nail, 6);
            }
        }
    }

    // =====================================================================================================================
    /// <summary>Missing 3-tab shingles. Frame: on the roof plane, +X along the eave, +Z up-slope, +Y roof normal. Needs the ladder.</summary>
    public class ShinglesDefect : RepairDefect
    {
        const float Exp = 0.127f, Tab = 0.3048f;
        public override string Title => "Missing roof shingles";
        public override string Symptom => "A few shingles blew off the front of the roof in that storm. I found them in the yard.";
        public override string Observation => Has("nailed") ? "New shingles are in, slots lined up with the course below" + (Has("cement") ? ", tabs stuck down." : ". The tabs are loose until the sun seals them.")
            : Has("slid") ? "New shingles are slid in under the course above, not nailed."
            : Has("removed") ? "Torn pieces and old nails are out. Clean felt, ready for shingles."
            : "Five tabs are gone over two courses. Bare felt and nail heads show, and the tab above is lifted.";
        public override bool IsFixed => Has("nailed");
        public override float BookHours => 1.5f;
        public override bool NeedsLadder => true;
        protected override float SkipPenalty => Has("cement") ? 0f : 0.2f;
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0.08f, 0.33f), new Vector3(1.6f, 0.3f, 1.0f));

        protected override void DefineActions()
        {
            Act("seal", "Slide the flat bar under the course above and pop the sealant strip", C.FlatBar, 2f, () => !Has("seal") && !Has("removed"), () => { Set("seal"); return "Seal is broken; the tabs above lift freely."; }).Snd("knock");
            Act("pull", "Pull the nails holding the torn pieces (and the row above that pins them)", C.FlatBar, 3f, () => !Has("removed"), () =>
            {
                Set("removed");
                if (!Has("seal")) { Lose(0.15f); return "Without breaking the seal first, the shingle above tore when you levered on it."; }
                return "Damaged pieces and eight nails are out.";
            }).Snd("knock");
            Act("slide", "Slide two new 3-tab shingles in, slots offset half a tab from the course below", null, 3f, () => Has("removed") && !Has("slid"), () => { Set("slid"); return "Shingles are in, 5\" exposure."; }).Uses(C.Shingles, 2);
            Act("nail", "Four roofing nails each, just above the slots, under the course above", C.Hammer, 3f, () => Has("slid") && !Has("nailed"), () => { Set("nailed"); return "Eight nails, heads flat and covered."; }).Uses(C.RoofNails, 8).Snd("hammer");
            Act("cement", "Dab roofing cement under each new tab and the lifted tabs above, press them down", C.CaulkGun, 2f, () => Has("nailed") && !Has("cement"), () => { Set("cement"); return "Tabs are stuck down."; }).Uses(C.RoofCement).Snd("caulk");
        }

        protected override void BuildModel(RM m)
        {
            int felt = m.Mat(new Color(0.09f, 0.09f, 0.1f)), nail = m.Mat(SurfaceKind.Galvanized);
            float z0 = 0.2f;
            if (!Has("slid"))
            {
                m.Box(new Vector3(0, 0.004f, z0 + Exp / 2), new Vector3(Tab * 3, 0.002f, Exp), felt);
                m.Box(new Vector3(0, 0.004f, z0 + Exp * 1.5f), new Vector3(Tab * 2, 0.002f, Exp), felt);
                if (!Has("removed"))
                {
                    int old = m.Mat(new Color(0.24f, 0.23f, 0.23f));
                    for (int i = 0; i < 5; i++) m.Cyl(new Vector3(-0.38f + i * 0.19f, 0.006f, z0 + Exp + 0.015f), 0.0045f, 0.002f, nail, 8);
                    m.Box(new Vector3(-0.3f, 0.007f, z0 + Exp * 0.75f), new Vector3(0.2f, 0.004f, Exp * 0.5f), Quaternion.Euler(0, 12f, 0), old);          // torn remnant
                    var lift = Quaternion.Euler(Has("seal") ? -4f : -16f, 0, 0);
                    m.Box(new Vector3(0, 0.012f, z0 + Exp * 2.5f) + lift * new Vector3(0, 0, -Exp / 2 + 0.02f), new Vector3(Tab - 0.006f, 0.004f, Exp), lift, old);
                }
                return;
            }
            int shingle = m.Mat(new Color(0.2f, 0.2f, 0.215f)), shingle2 = m.Mat(new Color(0.17f, 0.17f, 0.185f));
            for (int i = 0; i < 3; i++) m.Box(new Vector3((i - 1) * Tab, 0.006f, z0 + Exp / 2), new Vector3(Tab - 0.006f, 0.004f, Exp), i % 2 == 0 ? shingle : shingle2);
            for (int i = 0; i < 2; i++) m.Box(new Vector3((i - 0.5f) * Tab, 0.009f, z0 + Exp * 1.5f), new Vector3(Tab - 0.006f, 0.004f, Exp), i % 2 == 0 ? shingle2 : shingle);
            if (!Has("nailed")) for (int i = 0; i < 4; i++) m.Cyl(new Vector3(-0.3f + i * 0.2f, 0.012f, z0 + Exp * 2 - 0.01f), 0.0045f, 0.002f, nail, 8);
        }
    }

    // =====================================================================================================================
    /// <summary>Failed caulk: exterior window sill joint (frame +Z outdoors, joint along X) or tub-to-wall joint (+X along the joint, +Z into the room).</summary>
    public class CaulkDefect : RepairDefect
    {
        bool Tub => Spot == HouseSpot.TubCaulk;
        float L => Tub ? 1.524f : (House != null && House.WindowWidth > 0.2f ? House.WindowWidth : 1.2f);

        public override string Title => Tub ? "Failed tub caulk" : "Cracked window caulk";
        public override string Symptom => Tub ? "The caulk around the bathtub is black and peeling. Water is getting behind it."
                                              : "There's a draft at the living room window and the caulk outside is all cracked.";
        public override string Observation => Has("beaded") ? (Has("tooled") ? "A smooth, even bead fills the joint." : "A fresh bead, lumpy and untooled, with gaps at the edges.")
            : Has("removed") ? (Has("cleaned") ? "Joint is open, clean and dry." : "Old caulk is cut out. Residue and dust are still in the joint.")
            : Tub ? "The bead has pulled off the tub in places and is spotted black with mildew." : "The bead under the sill is brittle and cracked open with gaps you can get a fingernail in.";
        public override bool IsFixed => Has("beaded");
        public override float BookHours => Tub ? 1f : 0.75f;
        protected override float SkipPenalty => (Has("tooled") ? 0f : 0.15f) + (Tub && !Has("filled") ? 0.1f : 0f);
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0, 0.03f), new Vector3(L + 0.2f, 0.3f, 0.16f));

        string Bead(bool silicone)
        {
            Set("beaded"); Set(silicone ? "silicone" : "acrylic");
            string note = "";
            if (!Has("removed")) { Lose(0.4f); note = " Caulk over failed caulk will not bond; it will peel with the old bead."; }
            else if (!Has("cleaned")) { Lose(0.25f); note = " The joint was still dusty, so the bond is weak."; }
            if (Tub && !silicone) { S.wrongRepair = true; Lose(0.45f); note += " Acrylic caulk in a wet tub joint will mildew and let go within months."; }
            if (!Tub && silicone) { Lose(0.1f); note += " Pure silicone outdoors can't be painted to match the trim."; }
            return "Bead is in." + note;
        }

        protected override void DefineActions()
        {
            Act("cut", "Slice both edges of the old bead and pull it out", C.Knife, 3f, () => !Has("removed") && !Has("beaded"), () => { Set("removed"); Dirty(0.1f); return "Old caulk is out."; }).Snd("cut");
            Act("clean", Tub ? "Scrape the residue, kill the mildew with bleach, wipe with alcohol and let it dry" : "Scrape the residue, brush out the dust and wipe the joint", C.Bucket, 2f,
                () => Has("removed") && !Has("cleaned") && !Has("beaded"), () => { Set("cleaned"); S.mess = 0f; return "Joint is clean and dry."; }).Snd("scrape");
            if (Tub) Act("fill", "Fill the tub with water so the joint opens to its widest", null, 2f, () => !Has("filled") && !Has("beaded"), () => { Set("filled"); return "Tub is full; the joint has opened a hair."; }).Snd("water");
            Act("bead-sil", "Run a continuous bead of 100% silicone", C.CaulkGun, 3f, () => !Has("beaded"), () => Bead(true)).Uses(C.CaulkSilicone).Snd("caulk");
            Act("bead-acr", "Run a continuous bead of siliconized acrylic", C.CaulkGun, 3f, () => !Has("beaded"), () => Bead(false)).Uses(C.CaulkExt).Snd("caulk");
            Act("tool", "Tool the bead with a wet finger so it wets both sides of the joint", null, 2f, () => Has("beaded") && !Has("tooled"), () => { Set("tooled"); return Tub ? "Smooth concave bead. Leave the tub full until it skins over." : "Smooth concave bead."; });
        }

        protected override void BuildModel(RM m)
        {
            float l = L;
            var along = Quaternion.Euler(0, 0, 90f);     // cylinder axis Y -> X
            if (Has("beaded"))
            {
                int bead = m.Mat(Has("acrylic") ? new Color(0.96f, 0.95f, 0.92f) : new Color(0.99f, 0.99f, 0.99f));
                if (Has("tooled")) m.Box(new Vector3(0, 0, 0.002f), new Vector3(l, 0.009f, 0.004f), Quaternion.Euler(Tub ? 45f : 0f, 0, 0), bead);
                else for (int i = 0; i < 12; i++) m.Cyl(new Vector3(-l / 2 + (i + 0.5f) * l / 12, 0.002f * Mathf.Sin(i * 1.7f), 0.003f), along, 0.0035f + 0.0018f * Mathf.Abs(Mathf.Sin(i * 2.1f)), l / 12 * (i % 4 == 3 ? 0.8f : 1.02f), bead, 8);
                return;
            }
            if (Has("removed"))
            {
                m.Box(new Vector3(0, 0, 0.001f), new Vector3(l, 0.006f, 0.002f), m.Mat(Has("cleaned") ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.12f, 0.11f, 0.1f)));
                if (!Has("cleaned")) for (int i = 0; i < 7; i++) m.Box(new Vector3(-l / 2 + (i + 0.5f) * l / 7, 0.004f, 0.003f), new Vector3(0.03f, 0.004f, 0.003f), m.Mat(new Color(0.8f, 0.76f, 0.62f)));
                return;
            }
            int old = m.Mat(Tub ? new Color(0.85f, 0.83f, 0.74f) : new Color(0.8f, 0.76f, 0.6f)), gap = m.Mat(new Color(0.08f, 0.07f, 0.06f)), mold = m.Mat(new Color(0.1f, 0.11f, 0.09f));
            m.Box(new Vector3(0, 0, 0.0008f), new Vector3(l, 0.007f, 0.0016f), gap);
            for (int i = 0; i < 9; i++)
            {
                float x = -l / 2 + (i + 0.5f) * l / 9, len = l / 9 * (i % 3 == 1 ? 0.55f : 0.9f);
                var q = Quaternion.Euler(0, 0, 90f + (i % 3 == 2 ? 3f : 0f));
                m.Cyl(new Vector3(x, i % 3 == 2 ? -0.004f : 0f, 0.004f + (i % 3 == 2 ? 0.004f : 0f)), q, 0.005f, len, old, 8);
                if (Tub && i % 2 == 0) for (int k = 0; k < 3; k++) m.Box(new Vector3(x - 0.03f + k * 0.03f, 0.001f, 0.0092f), new Vector3(0.012f, 0.006f, 0.001f), mold);
            }
        }
    }

    // =====================================================================================================================
    /// <summary>Clogged gutter. Cause 0 = debris + a hanger pulled loose, 1 = debris + plugged downspout elbow.
    /// Frame: centre of the top of the 5" K-style gutter, +X along it, +Z away from the fascia. Needs the ladder.</summary>
    public class GutterDefect : RepairDefect
    {
        public const int LooseHanger = 0, CloggedElbow = 1;
        public override string Title => "Clogged gutter";
        public override string Symptom => Cause == LooseHanger ? "The front gutter pours over the edge when it rains and it's sagging away from the house in one spot."
                                                               : "Water sheets over the front gutter in every rain. Nothing comes out of the downspout.";
        public override string Observation => !Has("scooped") ? "The trough is packed with wet leaves, twigs and shingle grit" + (Cause == LooseHanger && !Has("hanger") ? ". One hanger has pulled out of the fascia." : ".")
            : Has("backed") && !Has("elbow") ? "Flush water is standing in the trough: it isn't getting down the downspout."
            : Has("flushed") ? "Clean trough; water runs straight to the outlet." : "Debris is out. A film of grit is left in the bottom."
              + (Cause == LooseHanger && !Has("hanger") ? " One hanger is still loose." : "");
        public override bool IsFixed => Has("scooped") && (Cause == CloggedElbow ? Has("elbow") : Has("hanger"));
        public override float BookHours => Cause == CloggedElbow ? 2f : 1.5f;
        public override bool NeedsLadder => true;
        protected override float SkipPenalty => Has("flushed") ? 0f : 0.15f;
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, -0.02f, 0), new Vector3(2.6f, 0.35f, 0.4f));

        protected override void DefineActions()
        {
            Act("scoop", "Scoop the debris into the bucket, working toward the downspout", C.Scoop, 5f, () => !Has("scooped"), () => { Set("scooped"); return "Two buckets of muck out."; }).Snd("scrape");
            Act("flush", "Flush the gutter toward the downspout with the hose", C.Hose, 3f, null, () =>
            {
                if (!Has("scooped")) { Dirty(0.25f); Lose(0.1f); return "The hose just washes the leaves into the outlet and packs the downspout. Scoop first."; }
                if (Cause == CloggedElbow && !Has("elbow")) { Set("backed"); return "Water backs up at the outlet and nothing comes out at the ground: the lower elbow is plugged."; }
                Set("flushed"); Clear("backed");
                return "Water runs clean and pours out of the bottom of the downspout.";
            }).Diag().Snd("water");
            Act("elbow", "Take out the two screws, pull the lower elbow off the downspout and clear the plug", C.Screwdriver, 4f, () => Cause == CloggedElbow && Has("backed") && !Has("elbow"), () =>
            { Set("elbow"); Clear("flushed"); Clear("backed"); return "A fist of matted leaves comes out. Elbow is back on, screws in. Flush again to prove it."; }).Snd("screw");
            Act("hanger", "Screw a new hidden hanger through the back of the gutter into the rafter tail", C.Drill, 3f, () => Cause == LooseHanger && !Has("hanger"), () => { Set("hanger"); return "Gutter is pulled back tight to the fascia."; }).Uses(C.Hanger).Snd("drill");
        }

        protected override void BuildModel(RM m)
        {
            if (!Has("scooped"))
            {
                var cols = new[] { new Color(0.36f, 0.24f, 0.12f), new Color(0.45f, 0.33f, 0.14f), new Color(0.25f, 0.2f, 0.1f), new Color(0.5f, 0.42f, 0.2f) };
                m.Box(new Vector3(0, -0.025f, 0), new Vector3(2.4f, 0.04f, 0.1f), m.Mat(new Color(0.16f, 0.12f, 0.08f)));
                for (int i = 0; i < 46; i++)
                {
                    float x = -1.2f + i * 2.4f / 45f, z = 0.045f * Mathf.Sin(i * 2.7f), y = 0.004f + 0.022f * Mathf.Abs(Mathf.Sin(i * 1.3f));
                    m.Box(new Vector3(x, y, z + (i % 9 == 0 ? 0.05f : 0f)), new Vector3(0.07f, 0.004f, 0.045f), Quaternion.Euler(i * 23f % 40f - 20f, i * 71f, i * 37f % 50f - 25f), m.Mat(cols[i % 4]));
                    if (i % 6 == 0) m.Tube(new Vector3(x, y + 0.01f, -0.05f), new Vector3(x + 0.16f, y + 0.02f, 0.06f), 0.003f, m.Mat(cols[2]), 5);
                }
            }
            else if (Has("backed") && !Has("elbow")) m.Box(new Vector3(0, -0.03f, 0), new Vector3(2.4f, 0.004f, 0.1f), m.Mat(new Color(0.35f, 0.42f, 0.45f)));
            else if (!Has("flushed")) m.Box(new Vector3(0, -0.08f, 0), new Vector3(2.4f, 0.003f, 0.07f), m.Mat(new Color(0.2f, 0.17f, 0.13f)));
            if (Cause == LooseHanger)
            {
                int al = m.Mat(SurfaceKind.Galvanized);
                if (Has("hanger")) m.Box(new Vector3(0.7f, -0.006f, 0), new Vector3(0.02f, 0.004f, 0.125f), al);
                else
                {
                    m.Box(new Vector3(0.7f, 0.02f, 0.01f), new Vector3(0.02f, 0.004f, 0.125f), Quaternion.Euler(-22f, 0, 0), al);
                    m.Tube(new Vector3(0.7f, 0.03f, -0.07f), new Vector3(0.7f, 0.05f, -0.02f), 0.003f, al, 6);
                }
            }
        }
    }

    // =====================================================================================================================
    /// <summary>Broken 5'x5'x4" sidewalk square. Frame: centre of the top face, +Z toward the house; the walk runs along X,
    /// so the neighbouring squares form two sides and 2x4 forms go on the two Z edges.</summary>
    public class SidewalkDefect : RepairDefect
    {
        const float Sq = 1.524f, T = 0.1016f;
        public const int Bags = 14, GravelBags = 8;      // 25 sq ft x 4" = 8.3 cu ft = 0.31 cu yd; 80-lb bag yields 0.6 cu ft

        public override string Title => "Broken sidewalk square";
        public override string Symptom => "The city sent a notice about the sidewalk out front. One square is cracked in pieces and heaved up. Somebody's going to trip.";
        public override string Observation =>
            Has("poured") ? (Has("broom") ? (Has("stripped") ? "New square, broom finished, forms stripped and edges backfilled." : "Broom finished. Forms are still on.")
                             : Has("screed") ? (Has("float") ? (Drying ? "Floated. Bleed water is still shining on the surface." : "Floated; the sheen is gone, ready to edge and broom.") : "Struck off level with the forms. Not floated yet.")
                             : "Concrete is in the forms, heaped above them.")
            : Has("forms") ? "2x4 forms staked on both open sides, tops level with the neighbouring squares" + (Has("base") ? ", gravel base compacted." : ". Bare dirt in the bottom.")
            : Has("removed") ? (Has("base") ? "2\" of compacted gravel in the hole. No forms yet." : "Hole down to dirt, 6\" below the walk.")
            : Has("broken") ? "The slab is broken into carryable chunks." : "The square is cracked into four pieces; one corner is heaved 1-1/2\" above its neighbour.";
        public override bool IsFixed => Has("poured") && Has("screed");
        public override float BookHours => 6f;
        protected override float SkipPenalty => (Has("float") ? 0f : 0.15f) + (Has("edge") ? 0f : 0.1f) + (Has("joint") ? 0f : 0.05f) + (Has("broom") ? 0f : 0.2f) + (Has("stripped") ? 0f : 0.1f);
        protected override Bounds ColliderBox => new Bounds(Vector3.zero, new Vector3(1.7f, 0.5f, 1.7f));
        protected override void FirstInit() { S.mess = 0f; }

        protected override void DefineActions()
        {
            Act("break", "Break the slab into pieces you can lift", C.Sledge, 6f, () => !Has("broken"), () => { Set("broken"); Dirty(0.3f); return "Slab is in a dozen chunks."; }).Snd("brick");
            Act("remove", "Lever out the chunks, load them in the van and dig 2\" deeper for base", C.Shovel, 6f, () => Has("broken") && !Has("removed"), () => { Set("removed"); return "Hole is clean, 6\" deep, edges of the old squares undamaged."; }).Snd("shovel");
            Act("base", $"Spread {GravelBags} bags of gravel 2\" deep and tamp it hard", C.Tamper, 5f, () => Has("removed") && !Has("base") && !Has("poured"), () => { Set("base"); return "Base is flat and doesn't give under your heel."; }).Uses(C.GravelBag, GravelBags).Snd("gravel");
            Act("forms", "Set 2x4 forms on the two open sides, staked outside, tops flush with the old squares", C.Hammer, 5f, () => Has("removed") && !Has("forms") && !Has("poured"), () => { Set("forms"); return "Forms are straight, flush and solid."; })
                .Uses(C.Stud, 2).Uses(C.Stake, 4).Snd("hammer");
            Act("pour", $"Mix {Bags} bags of 80-lb concrete (about 3 qt water each) and place it, spading along the forms", C.MixHoe, 10f, () => Has("forms") && !Has("poured"), () =>
            {
                Set("poured");
                if (!Has("base")) { Lose(0.3f); return "Concrete is in — straight onto soft dirt. With no compacted base it will settle and crack again."; }
                return "Concrete is in, a little above the forms.";
            }).Uses(C.Concrete80, Bags).Snd("pour");
            Act("screed", "Strike it off with a straight 2x4 riding the forms, sawing side to side", null, 4f, () => Has("poured") && !Has("screed"), () => { Set("screed"); return "Flat and full to the top of the forms."; }).Snd("scrape");
            Act("float", "Float the surface to push the stone down and bring up cream", C.MagFloat, 3f, () => Has("screed") && !Has("float") && !Has("broom"), () => { Set("float"); return "Floated. Wait for the bleed water to go."; }).Wait(40f).Snd("scrape");
            Act("edge", "Run the edger along both forms", C.Edger, 2f, () => Has("screed") && !Has("edge"), () => { Set("edge"); return "Rounded edges that won't chip."; }).Dry().Snd("scrape");
            Act("joint", "Tool the joints against the old squares to match the 5' pattern", C.Groover, 2f, () => Has("screed") && !Has("joint"), () => { Set("joint"); return "Joints tooled."; }).Dry().Snd("scrape");
            Act("broom", "Pull a light broom finish across the direction of travel", C.Broom, 2f, () => Has("screed") && !Has("broom"), () => { Set("broom"); return "Even broom texture. Keep feet off it for a day."; }).Dry().Wait(240f).Snd("roll");
            Act("strip", "Pull the stakes and forms, backfill and rake the edges", C.Hammer, 3f, () => Has("broom") && Has("forms") && !Has("stripped"), () => { Set("stripped"); S.mess = 0f; return "Forms are off, edges backfilled, site raked clean."; }).Dry().Snd("knock");
        }

        protected override void BuildModel(RM m)
        {
            House.SetFixtureVisible(Spot, false);
            int conc = m.Mat(SurfaceKind.Concrete), dirt = m.Mat(SurfaceKind.Dirt);
            if (!Has("broken"))
            {
                float cx = 0.12f, cz = -0.18f, g = 0.012f;
                float[] tilt = { 3.5f, -1.5f, -2.5f, 1f }; float[] lift = { 0.035f, 0.004f, 0.0f, 0.012f };
                int k = 0;
                foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                {
                    float x0 = sx < 0 ? -Sq / 2 : cx + g, x1 = sx < 0 ? cx - g : Sq / 2, z0 = sz < 0 ? -Sq / 2 : cz + g, z1 = sz < 0 ? cz - g : Sq / 2;
                    m.Box(new Vector3((x0 + x1) / 2, -T / 2 + lift[k], (z0 + z1) / 2), new Vector3(x1 - x0, T, z1 - z0), Quaternion.Euler(tilt[k], 0, -tilt[(k + 1) % 4]), conc);
                    k++;
                }
                m.Box(new Vector3(0, -T - 0.02f, 0), new Vector3(Sq, 0.02f, Sq), dirt);
                return;
            }
            if (!Has("removed"))
            {
                m.Box(new Vector3(0, -T - 0.02f, 0), new Vector3(Sq, 0.02f, Sq), dirt);
                for (int i = 0; i < 16; i++)
                {
                    float x = -0.57f + (i % 4) * 0.38f + 0.05f * Mathf.Sin(i * 3.1f), z = -0.57f + (i / 4) * 0.38f + 0.05f * Mathf.Cos(i * 1.9f);
                    m.Box(new Vector3(x, -T / 2 + 0.01f * (i % 3), z), new Vector3(0.3f + 0.05f * (i % 2), T, 0.28f), Quaternion.Euler(i * 7f % 16f - 8f, i * 31f, i * 11f % 14f - 7f), conc);
                }
                return;
            }
            float bottom = -T - 0.05f;
            if (!Has("poured"))
            {
                m.Box(new Vector3(0, bottom - 0.01f, 0), new Vector3(Sq, 0.02f, Sq), dirt);
                foreach (float sx in new[] { -1f, 1f }) m.Box(new Vector3(sx * (Sq / 2 - 0.002f), -T / 2 - 0.025f, 0), new Vector3(0.004f, T + 0.05f, Sq), conc);   // cut faces of the old squares
                if (Has("base")) m.Box(new Vector3(0, -T - 0.025f, 0), new Vector3(Sq, 0.05f, Sq), m.Mat(SurfaceKind.Gravel));
            }
            else
            {
                bool cured = Has("broom") && !Drying;
                int top = Has("broom") ? m.Mat(SurfaceKind.BroomedConcrete, cured ? Color.white : new Color(0.72f, 0.72f, 0.72f)) : m.Mat(SurfaceKind.WetConcrete);
                m.Box(new Vector3(0, -T / 2, 0), new Vector3(Sq - 0.004f, T, Sq - 0.004f), top);
                if (!Has("screed")) for (int i = 0; i < 9; i++) m.Box(new Vector3(-0.5f + (i % 3) * 0.5f, 0.012f, -0.5f + (i / 3) * 0.5f), new Vector3(0.42f, 0.03f + 0.012f * (i % 2), 0.4f), Quaternion.Euler(0, i * 17f, 0), top);
                if (Has("float") && !Has("broom") && Drying) m.Box(new Vector3(0, 0.0006f, 0), new Vector3(Sq * 0.8f, 0.001f, Sq * 0.8f), m.Mat(new Color(0.5f, 0.52f, 0.54f)));
                int line = m.Mat(new Color(0.3f, 0.3f, 0.3f));
                if (Has("edge")) foreach (float sz in new[] { -1f, 1f }) m.Box(new Vector3(0, 0.0005f, sz * (Sq / 2 - 0.012f)), new Vector3(Sq, 0.001f, 0.02f), m.Mat(SurfaceKind.TroweledConcrete));
                if (Has("joint")) foreach (float sx in new[] { -1f, 1f }) m.Box(new Vector3(sx * (Sq / 2 - 0.004f), 0.0006f, 0), new Vector3(0.008f, 0.001f, Sq), line);
            }
            if (Has("forms") && !Has("stripped"))
            {
                int pine = m.Mat(SurfaceKind.PineLumber);
                foreach (float sz in new[] { -1f, 1f })
                {
                    m.Box(new Vector3(0, -0.0445f, sz * (Sq / 2 + 0.019f)), new Vector3(Sq + 0.3f, 0.089f, 0.038f), Quaternion.identity, pine, Vector3.right);
                    foreach (float sx in new[] { -0.6f, 0.6f }) m.Box(new Vector3(sx, -0.17f, sz * (Sq / 2 + 0.057f)), new Vector3(0.038f, 0.4f, 0.038f), Quaternion.identity, pine, Vector3.up);
                }
            }
        }
    }
}
