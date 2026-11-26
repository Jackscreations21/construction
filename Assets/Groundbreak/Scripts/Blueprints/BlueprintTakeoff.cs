using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Takeoff for blueprints. Wood-frame houses reuse <see cref="HousePlan"/>'s takeoff and correct it (joist size/spans, girders,
    /// rafters, garages, stairs, multi-unit separations). Everything else is computed per structural system: wood stud, post frame,
    /// pre-engineered steel (rigid frames), or CMU walls with steel bar joists + TPO. Prices are 2026 US contractor costs.</summary>
    public partial class Blueprint
    {
        static int ReplaceLine(Takeoff t, string prefix, TakeoffLine with)
        {
            int i = t.Lines.FindIndex(l => l.Item.StartsWith(prefix));
            if (i >= 0) t.Lines[i] = with; else t.Lines.Add(with);
            return i;
        }
        static void RemoveLine(Takeoff t, string prefix) => t.Lines.RemoveAll(l => l.Item.StartsWith(prefix));
        static TakeoffLine Line(BuildPhase p, string item, float qty, string unit, int cost) => new TakeoffLine { Phase = p, Item = item, Qty = qty, Unit = unit, UnitCost = cost };

        int JoistUnitCost(float spanFt) =>
            FloorJoist.StartsWith("2x8") ? 16 : FloorJoist.StartsWith("2x10") ? (spanFt > 14f ? 26 : 22) : FloorJoist.StartsWith("2x12") ? (spanFt > 16f ? 38 : 32) :
            Mathf.RoundToInt(spanFt * (FloorJoist.Contains("11-7/8") ? 3.7f : FloorJoist.Contains("14") ? 4.6f : 6.2f));

        int RafterUnitCost(float lenFt) => Mathf.RoundToInt(lenFt * (RafterSize.StartsWith("2x6") ? 0.95f : RafterSize.StartsWith("2x8") ? 1.25f : RafterSize.StartsWith("2x10") ? 1.75f : 2.4f));

        protected override Takeoff ComputeTakeoff()
        {
            var t = IsResidential && WallFrame == WallSystem.WoodStud ? ResidentialTakeoff() : BuildingTakeoff();
            t.Lines.RemoveAll(l => l.Qty <= 0f);
            return t;
        }

        // ------------------------------------------------------------------ houses

        Takeoff ResidentialTakeoff()
        {
            var t = base.ComputeTakeoff();
            var F = BuildPhase.Foundation; var Fr = BuildPhase.Framing; var R = BuildPhase.RoughIn; var Dw = BuildPhase.Drywall; var Fi = BuildPhase.Finish;
            float W = WidthFt, D = DepthFt, fq = FinishFactor(BaseQuality);
            // floor framing: joists front to back, one per 16" across, per span between bearing lines
            int decks = Floors - 1 + (Foundation == FoundationType.Slab ? 0 : 1);
            int perSpan = Mathf.FloorToInt(W * 12f / 16f) + 1;
            t.Joists = decks * (BearingLines + 1) * perSpan;
            ReplaceLine(t, "2x10 floor joists", Line(Fr, $"{FloorJoist} floor joists @ 16\" O.C. ({FloorSpanFt:0.#}' spans)", t.Joists, "pcs", JoistUnitCost(FloorSpanFt)));
            if (decks > 0)
            {
                t.Lines.Add(Line(Fr, "Rim board 1-1/8\" x 12'", Mathf.Ceil(decks * 2f * W / 12f), "pcs", 38));
                if (Foundation != FoundationType.Slab && BearingLines > 0)
                {
                    t.Lines.Add(Line(Fr, "Girder: 3-ply LVL 1-3/4x11-7/8 (lf)", BearingLines * W, "lf", 16));
                    t.Lines.Add(Line(F, Foundation == FoundationType.Basement ? "Steel lally columns + pads" : "Pier footings + CMU piers", BearingLines * (Mathf.Ceil(W / 8f) - 1), "pcs", 140));
                }
            }
            // roof
            float pf = Mathf.Sqrt(1f + Pitch * Pitch / 144f);
            float span = RidgeAlongY ? W : D, run = RidgeAlongY ? D : W;
            if (RoofFrame == RoofSystem.Rafters)
            {
                RemoveLine(t, "Roof trusses");
                int pairs = Mathf.FloorToInt(run * 12f / 16f) + 1;
                float len = Mathf.Ceil((span / 2f + 1f) * pf / 2f) * 2f;
                t.Lines.Add(Line(Fr, $"{RafterSize} rafters x {len:0}' @ 16\" O.C.", pairs * 2, "pcs", RafterUnitCost(len)));
                t.Lines.Add(Line(Fr, "Ridge board 2x12 + collar ties", Mathf.Ceil(run / 12f), "pcs", 46));
                t.Lines.Add(Line(Fr, "2x6 ceiling joists", pairs, "pcs", 14));
                t.Trusses = 0;
            }
            else if (RidgeAlongY)
            {
                t.Trusses = Mathf.FloorToInt(D * 12f / 24f) + 1;
                ReplaceLine(t, "Roof trusses", Line(Fr, $"Roof trusses {W:0}' span {Pitch}/12 @ 24\"", t.Trusses, "pcs", Mathf.RoundToInt(6f * W)));
            }
            if (Covering != RoofCovering.Shingles)
                ReplaceLine(t, "Architectural shingles", Line(Fr, "Standing-seam metal roofing (sq ft)", t.RoofSqFt, "sq ft", 9));
            // windows/doors at their real catalog prices
            OpeningLines(t, Fr, Fi);
            // garage
            float gar = GarageSqFt;
            if (gar > 0f)
            {
                float sharedLf = 0f;
                foreach (var w in Walls)
                {
                    if (w.Exterior) continue;
                    var mid = (w.A + w.B) / 2f; var n = new Vector2(-w.Dir.y, w.Dir.x) * 0.2f;
                    var a = RoomAt(w.Floor, mid + n); var b = RoomAt(w.Floor, mid - n);
                    if (a != null && b != null && (KindOf(a.Name) == RoomKind.Garage) != (KindOf(b.Name) == RoomKind.Garage)) sharedLf += w.LengthFt;
                }
                t.Lines.Add(Line(Dw, "5/8\" Type X at garage/house separation (IRC R302.6)", Mathf.Ceil(sharedLf * WallHeightFt / 48f * 1.1f), "sheets", 24));
                if (Foundation != FoundationType.Slab) t.Lines.Add(Line(F, "Garage slab 4\" + thickened edge (yd³)", Mathf.Ceil(gar * 4f / 12f / 27f * 1.1f), "yd³", 165));
                t.Lines.Add(Line(Fi, "Garage door openers 1/2 hp", GarageBays > 2 ? 2 : Mathf.Max(1, GarageBays / 2 + GarageBays % 2), "pcs", 279));
            }
            // stairs
            foreach (var s in Stairs)
            {
                t.Lines.Add(Line(Fr, $"Stair: 3 x 2x12 stringers, {s.Risers} risers {s.RiserIn:0.##}\" ({s.Name})", 1, "flight", 160 + s.Treads * 12));
                t.Lines.Add(Line(Fi, $"Stair treads, risers, handrail + balusters ({s.Name})", s.Treads, "treads", Mathf.RoundToInt(95 * fq)));
            }
            // multi-family
            if (Units > 1)
            {
                ScaleLine(t, "Kitchen cabinets", Units);
                ScaleLine(t, "Driveway", Units);
                int hv = t.Lines.FindIndex(l => l.Item.StartsWith("HVAC")); if (hv >= 0) { var l = t.Lines[hv]; l.Qty = Units; l.UnitCost = Mathf.RoundToInt(5000 + SqFt / Units * 3.5f); t.Lines[hv] = l; }
                int pl = t.Lines.FindIndex(l => l.Item.StartsWith("Plumbing rough")); if (pl >= 0) { var l = t.Lines[pl]; l.Qty = Units * (Baths2 + 2); t.Lines[pl] = l; }
                float sepSf = (Units - 1) * D * WallHeightFt * Floors * 2f;
                t.Lines.Add(Line(Dw, Category == "townhome" ? "2 layers 5/8\" Type X at townhouse common walls (2 x 1-hr, IRC R302.2)" : "5/8\" Type X at dwelling-unit separation (1-hr, IRC R302.3)", Mathf.Ceil(sepSf / 48f * (Category == "townhome" ? 2.2f : 1.1f)), "sheets", 24));
                t.Lines.Add(Line(Dw, "Sound batts (R-13) in separation walls", sepSf / 2f, "sq ft", 1));
                t.Lines.Add(Line(R, "Separate meters, panels + water services", Units - 1, "units", 4200));
            }
            if (SprinklersRequired) t.Lines.Add(Line(R, "NFPA 13D residential sprinklers", SqFt, "sq ft", 3));
            return t;
        }

        static void ScaleLine(Takeoff t, string prefix, float qty) { int i = t.Lines.FindIndex(l => l.Item.StartsWith(prefix)); if (i >= 0) { var l = t.Lines[i]; l.Qty = qty; t.Lines[i] = l; } }

        /// <summary>Replaces the base window/door allowance lines with one line per catalog product actually on the plan.</summary>
        void OpeningLines(Takeoff t, BuildPhase fr, BuildPhase fi)
        {
            RemoveLine(t, "Windows"); RemoveLine(t, "Exterior doors"); RemoveLine(t, "Interior doors");
            var counts = new Dictionary<string, int>(); var order = new List<string>();
            t.Windows = t.ExtDoors = t.IntDoors = 0;
            foreach (var o in Openings)
            {
                bool ext = Walls[o.Wall].Exterior;
                if (o.IsDoor) { if (ext) t.ExtDoors++; else t.IntDoors++; } else t.Windows++;
                string key = o.ProductId + (ext ? "" : "|int");
                if (!counts.ContainsKey(key)) { counts[key] = 0; order.Add(key); }
                counts[key]++;
            }
            foreach (var key in order)
            {
                var id = key.Split('|')[0]; var d = Catalog.Get(id); if (d == null) continue;
                bool interior = key.EndsWith("|int");
                t.Lines.Add(Line(interior ? fi : fr, d.Name + (d.Category == PieceCategory.GarageDoor ? " (+ track, springs, header)" : ""), counts[key], "pcs",
                    d.Price + (d.Category == PieceCategory.GarageDoor ? Mathf.RoundToInt(d.Size.x / Units.Foot * 40f) : 0)));
            }
        }

        // ------------------------------------------------------------------ everything else

        Takeoff BuildingTakeoff()
        {
            var t = new Takeoff();
            var F = BuildPhase.Foundation; var Fr = BuildPhase.Framing; var R = BuildPhase.RoughIn; var Dw = BuildPhase.Drywall; var Fi = BuildPhase.Finish;
            float W = WidthFt, D = DepthFt, H = WallHeightFt;
            float fq = FinishFactor(BaseQuality);
            bool outb = Group == PlanGroup.Outbuilding, ind = Group == PlanGroup.Industrial, comm = Group == PlanGroup.Commercial;
            void L(BuildPhase p, string item, float qty, string unit, int cost) { if (qty > 0) t.Lines.Add(Line(p, item, qty, unit, p == Fi ? Mathf.RoundToInt(cost * fq) : cost)); }

            t.FootprintSqFt = W * D; t.SqFt = SqFt; t.PerimeterFt = 2f * (W + D); t.Corners = 4;
            int intWalls = 0;
            foreach (var w in Walls)
            {
                if (w.Exterior) { if (MezzanineFt <= 0f || w.Floor == 0) t.ExtWallLf += w.LengthFt; }
                else { t.IntWallLf += w.LengthFt; intWalls++; }
            }
            foreach (var o in Openings) if (Walls[o.Wall].Exterior) t.OpeningSqFt += o.WidthFt * o.HeightFt;
            float wallH = MezzanineFt > 0f ? H : H;   // exterior LF already per floor for stacked floors
            float span = RoofSpanFt * (InteriorColumnLines + 1), len = RidgeAlongY ? D : W;
            if (Roof == RoofType.Flat) len = Mathf.Max(W, D);
            float pf = Roof == RoofType.Flat ? 1f : Mathf.Sqrt(1f + Pitch * Pitch / 144f);
            float rise = Roof == RoofType.Gable || Roof == RoofType.Gambrel ? span / 2f * Pitch / 12f : Roof == RoofType.Shed ? span * Pitch / 12f : 0f;
            float gableSf = Roof == RoofType.Gable || Roof == RoofType.Gambrel ? span * rise : Roof == RoofType.Shed ? span * rise : 0f; // two triangles (or two shed-end trapezoid tops)
            t.ExtWallNetSqFt = t.ExtWallLf * wallH + gableSf - t.OpeningSqFt;
            float oh = Walls2Overhang;
            t.RoofSqFt = Roof == RoofType.Flat ? W * D : (W + 2f * oh) * (D + 2f * oh) * pf;
            t.RoofSquares = t.RoofSqFt / 100f;
            float area = W * D;
            float floorArea = SqFt;

            // ---------------- foundation
            float footing = 0f, slab = 0f, piers = 0f;
            switch (Foundation)
            {
                case FoundationType.Skids:
                    L(F, "Compacted 3/4\" gravel pad, 6\" deep (yd³)", Mathf.Ceil((W + 2f) * (D + 2f) * 0.5f / 27f * 1.1f), "yd³", 48);
                    L(F, "4x6 PT skids", Mathf.Ceil(W / 4f) + 1, $"pcs {D:0}'", Mathf.RoundToInt(D * 2.6f));
                    break;
                case FoundationType.PostFrame:
                {
                    int bays = Mathf.CeilToInt(len / 8f);
                    int posts = 2 * (bays + 1) + 2 * Mathf.Max(0, Mathf.CeilToInt(span / 8f) - 1);
                    piers = posts * 2.1f;
                    L(F, "Post holes 24\" dia x 4' + 8\" concrete footer pads", posts, "holes", 35);
                    if (SlabIn > 0f) slab = area * SlabIn / 12f;
                    t.Studs = 0;
                    break;
                }
                default:
                    if (outb) footing = t.PerimeterFt * 1f * 1.5f;                              // thickened edge 12"x18"
                    else { footing = t.PerimeterFt * 2f * 1f + t.PerimeterFt * (8f / 12f) * 3f; } // 24x12 footing + 8"x36" frost wall
                    slab = area * SlabIn / 12f;
                    break;
            }
            if (RoofFrame == RoofSystem.RigidFrame) { int frames = Mathf.CeilToInt(len / 25f) + 1; footing += frames * 2 * 5f * 5f * 1.5f; footing += InteriorColumnLines * frames * 4f * 4f * 1.5f; }
            if (RoofFrame == RoofSystem.BarJoist && InteriorColumnLines > 0) footing += InteriorColumnLines * (Mathf.CeilToInt(len / 25f) + 1) * 5f * 5f * 1.5f;
            t.ConcreteYards = (footing + slab + piers) / 27f;
            if (t.ConcreteYards > 0f)
            {
                L(F, SlabIn >= 6f ? "Ready-mix concrete 4000 psi" : "Ready-mix concrete 3000 psi", Mathf.Ceil(t.ConcreteYards * 1.05f), "yd³", SlabIn >= 6f ? 178 : 165);
                if (footing > 0f) L(F, "#4 rebar x 20' (footings)", Mathf.Ceil(t.PerimeterFt * 3f / 20f * 1.1f), "pcs", 9);
                if (slab > 0f)
                {
                    if (SlabIn >= 5f) L(F, "#4 rebar @ 18\" each way (slab)", Mathf.Ceil(2f * area / 1.5f / 20f * 1.1f), "pcs", 9);
                    else L(F, "6x6 W1.4 welded wire mesh (sq ft)", area, "sq ft", 1);
                    L(F, "4\" gravel base + 10 mil vapor barrier", area, "sq ft", 1);
                    if (SlabIn >= 5f) L(F, "Saw-cut control joints + curing", area, "sq ft", 1);
                }
                if (Foundation != FoundationType.PostFrame) L(F, WallFrame == WallSystem.SteelFrame ? "Anchor rods 3/4\" x 18\" (frame columns)" : "Anchor bolts 1/2\" x 10\"", Mathf.Ceil(t.PerimeterFt / 6f) + 8, "pcs", WallFrame == WallSystem.SteelFrame ? 9 : 2);
            }
            if (MezzanineFt > 0f)
            {
                float mezz = 0f; foreach (var r in Rooms) if (r.Floor == 1 && KindOf(r.Name) != RoomKind.Void && KindOf(r.Name) != RoomKind.Stair) mezz += r.Ft.width * r.Ft.height;
                int perSpan = Mathf.FloorToInt(mezz / Mathf.Max(1f, FloorSpanFt) * 12f / 16f) + 1;
                L(Fr, $"{FloorJoist} loft/mezzanine joists @ 16\" ({FloorSpanFt:0.#}' spans)", perSpan, "pcs", JoistUnitCost(FloorSpanFt));
                L(Fr, "3/4\" T&G subfloor 4x8 (loft/mezzanine)", Mathf.Ceil(mezz / 32f * 1.05f), "sheets", 48);
                L(Fr, "Loft beam: 3-ply LVL + posts", Mathf.Ceil(mezz / FloorSpanFt), "lf", 18);
                t.Joists = perSpan; t.SubfloorSheets = Mathf.CeilToInt(mezz / 32f * 1.05f);
            }

            // ---------------- framing / shell
            switch (WallFrame)
            {
                case WallSystem.WoodStud:
                {
                    foreach (var w in Walls) t.Studs += Mathf.FloorToInt(w.LengthFt * 12f / 16f) + 1;
                    foreach (var o in Openings) t.Studs += 2;
                    t.Studs += 2 * t.Corners + 2 * intWalls;
                    t.PlateLf = 3f * (t.ExtWallLf + t.IntWallLf);
                    int studCost = StudSize == "2x6" ? (H <= 8f ? 8 : H <= 10f ? 11 : 15) : (H <= 8f ? 4 : H <= 10f ? 6 : 8);
                    L(Fr, $"{StudSize} studs {(H <= 8f ? "92-5/8\"" : H + "'")}", t.Studs, "pcs", studCost);
                    L(Fr, $"{StudSize} plates (12' stock, bottom PT)", Mathf.Ceil(t.PlateLf / 12f), "pcs", StudSize == "2x6" ? 11 : 7);
                    t.SheathingSheets = Mathf.CeilToInt(t.ExtWallNetSqFt / 32f * 1.10f);
                    L(Fr, outb && !Conditioned ? "LP SmartSide panel siding 4x8 (structural)" : "OSB 7/16\" wall sheathing 4x8", t.SheathingSheets, "sheets", outb && !Conditioned ? 58 : 16);
                    if (Foundation == FoundationType.Skids)
                    {
                        L(Fr, "2x6 PT floor joists @ 16\"", Mathf.FloorToInt(W * 12f / 16f) + 1, "pcs", 12);
                        L(Fr, "3/4\" PT plywood floor 4x8", Mathf.Ceil(area / 32f * 1.05f), "sheets", 62);
                    }
                    foreach (var o in Openings) if (o.WidthFt >= 8f && Walls[o.Wall].Exterior) L(Fr, $"LVL header 3-1/2\" x 11-7/8\" x {o.WidthFt + 1f:0}'", 1, "pcs", Mathf.RoundToInt((o.WidthFt + 1f) * 14f));
                    WoodRoof(t, L, Fr, span, len, pf);
                    break;
                }
                case WallSystem.PostFrame:
                {
                    int bays = Mathf.CeilToInt(len / 8f);
                    int posts = 2 * (bays + 1) + 2 * Mathf.Max(0, Mathf.CeilToInt(span / 8f) - 1);
                    L(Fr, $"3-ply 2x6 laminated columns {H + 4f:0}' (PT base)", posts, "pcs", Mathf.RoundToInt((H + 4f) * 9.5f));
                    L(Fr, "2x8 PT skirt board", Mathf.Ceil(t.PerimeterFt / 16f), "pcs 16'", 26);
                    int girtRows = Mathf.CeilToInt(H / 2f);
                    L(Fr, "2x4 wall girts @ 24\" (lf)", t.PerimeterFt * girtRows, "lf", 1);
                    L(Fr, $"Post-frame trusses {span:0}' span {Pitch}/12 @ 8' (double)", bays + 1, "pcs", Mathf.RoundToInt(17f * span));
                    float slope = (span / 2f + oh) * pf;
                    L(Fr, "2x4 purlins on edge @ 24\" (lf)", Mathf.Ceil(slope / 2f) * 2f * (len + 2f * oh), "lf", 1);
                    L(Fr, "Truss + knee braces, hangers, ring-shank nails", bays + 1, "sets", 45);
                    t.Trusses = bays + 1;
                    break;
                }
                case WallSystem.SteelFrame:
                {
                    int frames = Mathf.CeilToInt(len / 25f) + 1;
                    L(Fr, $"PEMB rigid frames {span:0}' clear span" + (InteriorColumnLines > 0 ? $" ({InteriorColumnLines} interior column lines)" : ""), frames, "frames", Mathf.RoundToInt(105f * span));
                    L(Fr, "Endwall columns + rafters", 2, "endwalls", Mathf.RoundToInt(55f * span));
                    float slope = (span / 2f + 1f) * pf;
                    L(Fr, "Z-purlins (roof) @ 5' (lf)", Mathf.Ceil(slope / 5f + 1f) * 2f * len, "lf", 5);
                    L(Fr, "Girts (walls) @ 7' + eave struts (lf)", Mathf.Ceil(H / 7f + 1f) * t.PerimeterFt, "lf", 5);
                    L(Fr, "X-bracing rods + flange braces", frames, "bays", 260);
                    t.Trusses = frames;
                    break;
                }
                case WallSystem.Cmu:
                {
                    float gross = t.ExtWallLf * (H + 2f);  // + 2' parapet
                    float net = gross - t.OpeningSqFt;
                    int blocks = Mathf.CeilToInt(net / 0.889f * 1.05f);
                    L(Fr, "8\" CMU block 8x8x16", blocks, "pcs", 3);
                    L(Fr, "Type S mortar mix 80 lb", Mathf.Ceil(blocks / 30f), "bags", 12);
                    L(Fr, "#5 rebar verticals @ 32\" + bond beams (20')", Mathf.Ceil(t.ExtWallLf / 2.67f * (H + 2f) / 20f + t.ExtWallLf * 3f / 20f), "pcs", 14);
                    L(Fr, "Cell grout (yd³)", Mathf.Ceil(net / 2.67f * 0.6f / 27f * 3f), "yd³", 190);
                    L(Fr, "Steel lintels / bond-beam lintels at openings", OpeningCountExt(), "pcs", 140);
                    break;
                }
            }
            // roof structure for steel/CMU
            if (RoofFrame == RoofSystem.BarJoist)
            {
                int joists = Mathf.FloorToInt(len / 5f) + 1;
                L(Fr, $"K-series bar joists {RoofSpanFt:0}' @ 5' O.C.", joists * (InteriorColumnLines + 1), "pcs", Mathf.RoundToInt(12f * RoofSpanFt));
                if (InteriorColumnLines > 0) L(Fr, "Joist girders + HSS columns", InteriorColumnLines * (Mathf.CeilToInt(len / 25f) + 1), "bays", 2600);
                L(Fr, "1-1/2\" type B metal roof deck, 22 ga", area, "sq ft", 3);
                L(Fr, "Polyiso R-25 + 1/2\" cover board", area, "sq ft", 3);
                L(Fr, "TPO 60-mil membrane, mechanically attached", area * 1.08f, "sq ft", 2);
                L(Fr, "Parapet coping + edge metal (lf)", t.PerimeterFt, "lf", 16);
                L(Fr, "Roof drains / scuppers + downspouts", Mathf.Ceil(area / 4000f) + 2, "pcs", 650);
            }
            else if (WallFrame == WallSystem.PostFrame || WallFrame == WallSystem.SteelFrame)
            {
                bool steelBldg = WallFrame == WallSystem.SteelFrame;
                L(Fr, steelBldg ? "26-ga PBR wall panels + trim" : "29-ga steel wall panels + trim", t.ExtWallNetSqFt, "sq ft", steelBldg ? 3 : 2);
                L(Fr, Covering == RoofCovering.StandingSeam ? "24-ga standing-seam roof (SSR) + clips" : steelBldg ? "26-ga PBR roof panels" : "29-ga steel roof panels", t.RoofSqFt, "sq ft", Covering == RoofCovering.StandingSeam ? 5 : steelBldg ? 3 : 2);
                L(Fr, "Ridge cap, eave/rake trim, closures, screws (lf)", 2f * len + 2f * t.PerimeterFt, "lf", 4);
                L(Fr, "Gutters + downspouts (lf)", 2f * len, "lf", 9);
                if (!Conditioned) L(Fr, "Roof condensation blanket (drip stop)", t.RoofSqFt, "sq ft", 1);
            }
            OpeningLines(t, Fr, Fi);

            // ---------------- MEP rough-in
            int plumbFx = 0; foreach (var fx in Fixtures) if (fx.Trade == Pl && fx.Kind != "WH" && fx.Kind != "GI") plumbFx++;
            if (plumbFx > 0)
            {
                L(R, "Plumbing rough-in: DWV + water per fixture", plumbFx, "fixtures", outb ? 650 : 1450);
                if (!outb) L(R, "Water + sewer service, meter, backflow (allowance)", 1, "lot", ind ? 14000 : 9000);
            }
            foreach (var fx in Fixtures)
            {
                switch (fx.Kind)
                {
                    case "WH": L(R, "Water heater " + fx.Note, 1, "pcs", fx.Note.Contains("100") ? 6500 : fx.Note.Contains("20 gal") ? 900 : 1600); break;
                    case "GI": L(R, "Grease interceptor " + fx.Note, 1, "pcs", 14000); break;
                    case "HOOD": L(R, "Kitchen hood + fire suppression + make-up air", 1, "system", 26000); break;
                    case "CU": L(R, "Walk-in cooler box + condensing unit", 1, "system", 16000); break;
                    case "RISER": L(R, "Sprinkler riser, backflow, FDC", 1, "pcs", 14000); break;
                    case "MINI": L(R, "Ductless mini-split heat pump (12k BTU)", 1, "system", 4200); break;
                    case "UH": L(R, "Gas unit heater + flue + gas pipe", 1, fx.Note.Contains("150") ? "pcs (150 MBH)" : "pcs (60 MBH)", fx.Note.Contains("150") ? 4200 : 2600); break;
                }
            }
            float tons = 0f; int rtus = 0, sups = 0, efs = 0; foreach (var fx in Fixtures) { if (fx.Kind == "RTU") { rtus++; tons += ParseTons(fx.Note); } if (fx.Kind == "SUP") sups++; if (fx.Kind == "EF") efs++; }
            if (rtus > 0) { L(R, $"Packaged rooftop units ({tons:0.#} tons total) + curbs + crane", rtus, "pcs", Mathf.RoundToInt(tons / rtus * 2600f + 4000f)); L(R, "Ductwork, diffusers, returns (conditioned sq ft)", ConditionedSqFt, "sq ft", 7); }
            if (efs > 0) L(R, "Exhaust fans", efs, "pcs", 260);
            float circuitsPer = outb ? 250f : ind ? 600f : 120f;
            string svc = Fixtures.Find(x => x.Kind == "PNL")?.Note ?? "";
            if (svc != "") L(R, "Electrical service / panel: " + svc, 1, "pcs", svc.Contains("800A") ? 32000 : svc.Contains("400A") ? 11000 : svc.Contains("200A") ? 4800 : svc.Contains("100A") ? 1500 : svc.Contains("60A") ? 950 : 600);
            L(R, "Branch circuits (wire, boxes, devices)", Mathf.Max(3f, Mathf.Ceil(floorArea / circuitsPer)), "circuits", outb ? 220 : 380);
            float whse = 0f; foreach (var r in Rooms) { var k = KindOf(r.Name); if (k == RoomKind.Warehouse || k == RoomKind.Shop || k == RoomKind.Barn || k == RoomKind.Garage || k == RoomKind.Stall || k == RoomKind.Shed || k == RoomKind.Loft) whse += r.Ft.width * r.Ft.height; }
            float office = Mathf.Max(0f, floorArea - whse);
            if (whse > 0f) L(R, whse > 3000f ? "LED high-bay fixtures (24k lm)" : "LED shop lights 4' (5k lm)", Mathf.Ceil(whse / (whse > 3000f ? 400f : 100f)), "pcs", whse > 3000f ? 240 : 55);
            if (office > 0f && !outb) L(R, "LED 2x4 troffers + emergency/exit lights", Mathf.Ceil(office / 70f), "pcs", 165);
            if (SprinklersRequired) L(R, "Wet-pipe fire sprinklers (NFPA 13)", floorArea, "sq ft", 4);
            if (!outb) L(R, "Fire alarm, smoke/heat detection, exit signs", floorArea, "sq ft", ind ? 1 : 2);

            // ---------------- insulation + drywall
            float condWallSf = 0f, condArea = ConditionedSqFt;
            if (condArea > 0f) condWallSf = t.ExtWallNetSqFt * Mathf.Clamp01(condArea / Mathf.Max(1f, area * (MezzanineFt > 0 ? 1f : Floors)));
            switch (WallFrame)
            {
                case WallSystem.WoodStud:
                    if (Conditioned) { t.WallInsulationSqFt = condWallSf; t.AtticInsulationSqFt = condArea; L(Dw, StudSize == "2x6" ? "R-21 wall batts" : "R-15 wall batts", condWallSf, "sq ft", 1); if (RoofFrame != RoofSystem.BarJoist) L(Dw, "R-49 attic blown-in", condArea, "sq ft", 1); }
                    break;
                case WallSystem.PostFrame:
                    if (Conditioned) { L(Dw, "R-19 wall batts + steel liner panel", condWallSf, "sq ft", 3); L(Dw, "R-38 ceiling (blown on steel liner)", condArea, "sq ft", 3); }
                    break;
                case WallSystem.SteelFrame:
                    L(Dw, "Vinyl-faced metal building insulation R-19 walls", t.ExtWallNetSqFt, "sq ft", 1);
                    L(Dw, "Vinyl-faced liner system R-30 roof", t.RoofSqFt, "sq ft", 2);
                    break;
                case WallSystem.Cmu:
                    if (condWallSf > 0f) L(Dw, "Furring + 2\" polyiso at CMU walls (R-13 ci)", condWallSf, "sq ft", 3);
                    break;
            }
            if (Finished)
            {
                float partH = Mathf.Min(H, comm ? 12f : 10f);
                float dwSf = 0f, actSf = 0f;
                if (condArea > 0f) dwSf += condWallSf;
                foreach (var w in Walls) if (!w.Exterior) dwSf += 2f * w.LengthFt * partH;
                foreach (var r in Rooms)
                {
                    if (!IsConditionedRoom(r)) continue; var k = KindOf(r.Name);
                    if (!comm && !ind || k == RoomKind.Restroom || k == RoomKind.CommKitchen || k == RoomKind.Janitor) dwSf += r.Ft.width * r.Ft.height; else actSf += r.Ft.width * r.Ft.height;
                }
                t.DrywallSheets = Mathf.CeilToInt(dwSf / 48f * 1.12f);
                L(Dw, comm || ind ? "5/8\" Type X gypsum 4x12" : "Drywall 1/2\" 4x12", t.DrywallSheets, "sheets", comm || ind ? 24 : 21);
                L(Dw, "Mud, tape, screws (per sheet)", t.DrywallSheets, "sheets", 4);
                if (comm || ind) L(Dw, "3-5/8\" 20-ga steel studs @ 16\" (partitions, lf of wall)", t.IntWallLf, "lf", 9);
                if (actSf > 0f) L(Fi, "2x4 acoustical ceiling grid + tile", actSf, "sq ft", 4);
            }

            // ---------------- finishes
            float vinylSf = 0f, tileSf = 0f, concSf = 0f, lvtSf = 0f;
            foreach (var r in Rooms)
            {
                var k = KindOf(r.Name); float a = r.Ft.width * r.Ft.height; if (k == RoomKind.Void || (k == RoomKind.Stair && r.Floor > 0)) continue;
                if (k == RoomKind.Restroom || k == RoomKind.CommKitchen || k == RoomKind.Cooler || k == RoomKind.Janitor) tileSf += a;
                else if (k == RoomKind.Warehouse || k == RoomKind.Shop || k == RoomKind.Garage || k == RoomKind.Barn || k == RoomKind.Stall || k == RoomKind.Storage || k == RoomKind.Mech || k == RoomKind.Shed || k == RoomKind.Loft) concSf += a;
                else if (k == RoomKind.Sales || k == RoomKind.Dining || k == RoomKind.Lobby) lvtSf += a; else vinylSf += a;
            }
            if (WallFrame == WallSystem.WoodStud && !(outb && !Conditioned)) L(Fi, outb ? "LP SmartSide lap siding + trim" : "Fiber-cement siding + trim", t.ExtWallNetSqFt, "sq ft", outb ? 5 : 7);
            if (WallFrame == WallSystem.WoodStud && outb && !Conditioned) L(Fi, "Exterior paint + trim (siding is the sheathing)", t.ExtWallNetSqFt, "sq ft", 2);
            if (WallFrame == WallSystem.Cmu) L(Fi, "Split-face CMU front + EIFS band, paint the rest", t.ExtWallNetSqFt, "sq ft", 6);
            if (Foundation != FoundationType.Skids && concSf > 0f) L(Fi, Foundation == FoundationType.PostFrame && SlabIn <= 0f ? "Gravel floor, compacted (sq ft)" : "Sealed concrete floor (sq ft)", concSf, "sq ft", 1);
            if (Foundation == FoundationType.Skids) L(Fi, "Shed ramp, trim, paint", 1, "set", 650);
            if (tileSf > 0f) L(Fi, "Quarry tile / epoxy floor + cove base", tileSf, "sq ft", 16);
            if (lvtSf > 0f) L(Fi, "LVT flooring (sales / dining)", lvtSf, "sq ft", 7);
            if (vinylSf > 0f && Finished) L(Fi, comm || ind ? "Carpet tile / LVT (offices)" : "LVP flooring", vinylSf, "sq ft", 6);
            if (Finished && (comm || ind)) L(Fi, "Paint walls + doors + frames", t.IntWallLf * Mathf.Min(H, 12f) * 2f + condWallSf, "sq ft", 1);
            int restrooms = 0; foreach (var r in Rooms) if (KindOf(r.Name) == RoomKind.Restroom) restrooms++;
            if (restrooms > 0) L(Fi, "Restroom accessories (grab bars, mirror, dispensers)", restrooms, "rooms", 1400);
            int fixtures = 0; foreach (var fx in Fixtures) if (fx.Kind == "WC" || fx.Kind == "LAV" || fx.Kind == "MS" || fx.Kind == "DF" || fx.Kind == "3CS" || fx.Kind == "HS" || fx.Kind == "KS" || fx.Kind == "TUB" || fx.Kind == "SHWR") fixtures++;
            if (fixtures > 0) L(Fi, outb ? "Plumbing fixtures + trim" : "Commercial plumbing fixtures (ADA) + trim", fixtures, "pcs", outb ? 420 : 780);
            if (Category == "restaurant") L(Fi, "Kitchen equipment package (line, refrigeration, smallwares)", 1, "allowance", 95000);
            if (Category == "gas-station") L(Fi, "C-store fixtures: coolers, gondolas, sales counter", 1, "allowance", 60000);
            if (Category == "retail" || Category == "strip-mall") L(Fi, "Sales counter + tenant signage allowance", Mathf.Max(1, Units), "spaces", 6500);
            if (Category == "office") L(Fi, "Millwork: reception desk, break-room cabinets", 1, "allowance", 14000);
            // site
            int stalls = Mathf.CeilToInt(ParkingRequired);
            if (stalls > 0) L(Fi, $"Asphalt parking {stalls} stalls + drive aisles, striping, ADA stall", stalls, "stalls", 3600);
            int ohDoors = 0; foreach (var o in Openings) if (o.IsOverhead) ohDoors++;
            if (ohDoors > 0) L(Fi, "Concrete aprons at overhead doors (6\")", ohDoors, "pcs", 1800);
            if (outb) L(Fi, "Gravel/concrete approach, grading, gutters to daylight", 1, "lot", Foundation == FoundationType.Skids ? 400 : 3500);
            else L(Fi, "Site utilities, storm drainage, landscaping (allowance)", 1, "lot", Mathf.RoundToInt(12000 + area * 1.5f));

            // ---------------- labor (US 2026 crew/sub rates)
            t.Labor[0] = Mathf.RoundToInt(t.ConcreteYards * 120f + (outb ? 800f : 4000f) + floorArea * (outb ? 1f : 2.5f));
            switch (WallFrame)
            {
                case WallSystem.WoodStud: t.Labor[1] = Mathf.RoundToInt(area * (outb ? 7f : 10f) + t.RoofSquares * 90f); break;
                case WallSystem.PostFrame: t.Labor[1] = Mathf.RoundToInt(area * 6.5f); break;
                case WallSystem.SteelFrame: t.Labor[1] = Mathf.RoundToInt(area * 7.5f + 6000f); break;
                default: t.Labor[1] = Mathf.RoundToInt(t.ExtWallLf * (H + 2f) * 11f + area * 4f); break;
            }
            t.Labor[2] = Mathf.RoundToInt(outb ? floorArea * 1.5f : ind ? whse * 3f + office * 14f : floorArea * 16f);
            t.Labor[3] = Mathf.RoundToInt(t.DrywallSheets * 34f + (Conditioned ? area * (outb ? 0.6f : 1.2f) : 0f) + (WallFrame == WallSystem.SteelFrame ? area * 0.8f : 0f));
            t.Labor[4] = Mathf.RoundToInt((outb ? floorArea * 1.5f : ind ? whse * 1.5f + office * 18f : floorArea * 20f) * fq + (comm || ind ? 8000f : 0f));
            return t;
        }

        float Walls2Overhang => WallFrame == WallSystem.SteelFrame ? 0.5f : Roof == RoofType.Flat ? 0f : 1f;

        int OpeningCountExt() { int n = 0; foreach (var o in Openings) if (Walls[o.Wall].Exterior) n++; return n; }

        static float ParseTons(string note) { if (string.IsNullOrEmpty(note)) return 0; int i = note.IndexOf("-ton"); if (i <= 0) return 0; float.TryParse(note.Substring(0, i), out var v); return v; }

        /// <summary>Off-street parking (typical zoning): retail 4/1000, office 3.5/1000, restaurant 10/1000, warehouse 1/2000 (min 4), gas station 1/250.</summary>
        public float ParkingRequired
        {
            get
            {
                if (UsesIrc) return 0f;
                float a = SqFt;
                switch (Category)
                {
                    case "restaurant": return Mathf.Max(8f, a * 10f / 1000f);
                    case "gas-station": return Mathf.Max(6f, a / 250f);
                    case "office": return Mathf.Max(5f, a * 3.5f / 1000f);
                    case "warehouse": case "flex": return Mathf.Max(4f, a / 2000f);
                    default: return Mathf.Max(5f, a * 4f / 1000f);
                }
            }
        }

        void WoodRoof(Takeoff t, System.Action<BuildPhase, string, float, string, int> L, BuildPhase Fr, float span, float len, float pf)
        {
            float oh = Walls2Overhang;
            if (RoofFrame == RoofSystem.Rafters)
            {
                int pairs = Mathf.FloorToInt(len * 12f / 16f) + 1;
                float rl = Roof == RoofType.Shed ? Mathf.Ceil((span + 2f * oh) * pf / 2f) * 2f : Mathf.Ceil((span / 2f + oh) * pf / 2f) * 2f;
                int n = Roof == RoofType.Shed ? pairs : pairs * 2;
                L(Fr, $"{RafterSize} rafters x {rl:0}' @ 16\" O.C.", n, "pcs", RafterUnitCost(rl));
                if (Roof != RoofType.Shed) L(Fr, "Ridge board + collar ties", Mathf.Ceil(len / 12f), "pcs", 40);
                t.Trusses = 0;
            }
            else if (RoofFrame == RoofSystem.Trusses)
            {
                t.Trusses = Mathf.FloorToInt(len * 12f / 24f) + 1;
                L(Fr, $"{(Roof == RoofType.Gambrel ? "Gambrel" : Roof == RoofType.Shed ? "Mono" : "Common")} roof trusses {span:0}' span {Pitch}/12 @ 24\"", t.Trusses, "pcs", Mathf.RoundToInt((Roof == RoofType.Gambrel ? 9f : 6f) * span + (span > 40f ? 4f * (span - 40f) : 0f)));
                L(Fr, "Truss bracing, hurricane ties, hangers", t.Trusses, "sets", 9);
            }
            t.RoofSheathingSheets = Mathf.CeilToInt(t.RoofSqFt / 32f * 1.08f);
            L(Fr, "OSB 7/16\" roof sheathing 4x8 (H-clips)", t.RoofSheathingSheets, "sheets", 16);
            if (Covering == RoofCovering.Shingles)
            {
                t.ShingleBundles = Mathf.CeilToInt(t.RoofSquares * 1.10f * 3f);
                L(Fr, "Architectural shingles (bundle)", t.ShingleBundles, "bundles", 42);
                L(Fr, "Synthetic underlayment + drip edge + ridge vent", t.RoofSquares, "squares", 38);
            }
            else L(Fr, Covering == RoofCovering.StandingSeam ? "Standing-seam metal roof (sq ft)" : "29-ga metal roof panels (sq ft)", t.RoofSqFt, "sq ft", Covering == RoofCovering.StandingSeam ? 9 : 3);
        }
    }
}
