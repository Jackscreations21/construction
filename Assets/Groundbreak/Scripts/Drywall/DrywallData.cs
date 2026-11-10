using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum DrywallKind { Regular, Ultralight, TypeX, MoistureResistant, MoldResistant }

    public class DrywallProduct
    {
        public string Id, Name;
        public DrywallKind Kind;
        public float WidthIn = 48f, LengthIn, ThickIn;
        public int Price;
        public Color Face;       // paper face color
        public float Width => Units.In(WidthIn);
        public float Length => Units.In(LengthIn);
        public float Thick => Units.In(ThickIn);
        public bool FireRated => Kind == DrywallKind.TypeX;
        public bool WetRated => Kind == DrywallKind.MoistureResistant || Kind == DrywallKind.MoldResistant;
        public string Short => $"{(ThickIn > 0.6f ? "5/8\"" : "1/2\"")} {KindLabel} 4x{LengthIn / 12f:0}";
        public string KindLabel => Kind == DrywallKind.Regular ? "regular" : Kind == DrywallKind.Ultralight ? "ultralight" : Kind == DrywallKind.TypeX ? "Type X" : Kind == DrywallKind.MoistureResistant ? "moisture-resistant" : "mold-resistant";
    }

    /// <summary>Joint compound types (data). Each coat must dry (or set) before the next.</summary>
    public class MudCompound
    {
        public string Id, Name; public float DryHours; public bool Setting;
    }

    /// <summary>Drywall code + finishing data (IRC R702.3, GA-216). Tweak to model other jurisdictions.</summary>
    public static class DrywallRules
    {
        public static float ScrewSpacingCeilingIn = 12f, ScrewSpacingWallIn = 16f;
        public static float ScrewEdgeMinIn = 0.375f;     // 3/8" from edges
        public static float ScrewEdgeMaxIn = 8f;         // first screw within 8" of a sheet end along a member
        public static float ButtStaggerIn = 16f;         // butt joints in adjacent rows at least 16" apart
        public static float FloorGapIn = 0.5f;           // bottom row held ~1/2" off the floor
        public static float WetAreaFeet = 3f;            // MR/mold-resistant board within this distance of a tub/shower
        public static readonly List<string> WetFixtureIds = new List<string> { "pl-tub60", "pl-shower48" };
        public static bool TypeXOn24InchCeilings = true; // 5/8" Type X on ceilings framed 24" O.C.
        /// <summary>Faces that must be fire-rated (garage separation walls, etc.): tag with <see cref="Drywall.TagFireRated"/>.</summary>
        public static readonly List<FramePlane> FireRatedFaces = new List<FramePlane>();
        public static float TextureDryHours = 8f;
        public static float SkimDryHours = 24f;
        public static float ReachAboveEye = 0.7f;        // hands reach this far above the eyes (stilts add 18")

        public static readonly List<MudCompound> Compounds = new List<MudCompound>
        {
            new MudCompound { Id = "dw-mud-ap", Name = "All-purpose joint compound (4.5 gal)", DryHours = 24f },
            new MudCompound { Id = "dw-mud-set45", Name = "Setting compound 45-min (18 lb bag)", DryHours = 1f, Setting = true },
        };
        public static MudCompound Compound(string id) => Compounds.Find(c => c.Id == id) ?? Compounds[0];
        /// <summary>Knife (inches) for coats 1..3.</summary>
        public static readonly float[] KnifeForCoat = { 6f, 10f, 12f };
        /// <summary>Band widths (inches, full width for flat joints) per coat.</summary>
        public static readonly float[] FlatWidth = { 6f, 10f, 12f }, ButtWidth = { 6f, 12f, 16f }, CornerSide = { 2.5f, 4f, 5f }, BeadSide = { 4f, 6f, 8f };
        public static readonly float[] SpotDia = { 1.75f, 2.5f, 3.2f };
    }

    public static partial class Drywall
    {
        public static readonly List<DrywallProduct> Products = new List<DrywallProduct>
        {
            P("dw-12-4x8", "Drywall 1/2\" regular 4x8", DrywallKind.Regular, 96, 0.5f, 15),
            P("dw-12-4x12", "Drywall 1/2\" regular 4x12", DrywallKind.Regular, 144, 0.5f, 21),
            P("dw-12ul-4x8", "Drywall 1/2\" ultralight 4x8", DrywallKind.Ultralight, 96, 0.5f, 17),
            P("dw-12ul-4x12", "Drywall 1/2\" ultralight 4x12", DrywallKind.Ultralight, 144, 0.5f, 24),
            P("dw-58x-4x8", "Drywall 5/8\" Type X (fire) 4x8", DrywallKind.TypeX, 96, 0.625f, 18),
            P("dw-58x-4x12", "Drywall 5/8\" Type X (fire) 4x12", DrywallKind.TypeX, 144, 0.625f, 26),
            P("dw-12mr-4x8", "Drywall 1/2\" moisture-resistant (green) 4x8", DrywallKind.MoistureResistant, 96, 0.5f, 19),
            P("dw-12mold-4x8", "Drywall 1/2\" mold-resistant (purple) 4x8", DrywallKind.MoldResistant, 96, 0.5f, 22),
        };

        static DrywallProduct P(string id, string name, DrywallKind k, float len, float t, int price)
        {
            Color face = k == DrywallKind.MoistureResistant ? new Color(0.56f, 0.70f, 0.56f) : k == DrywallKind.MoldResistant ? new Color(0.66f, 0.58f, 0.74f)
                       : k == DrywallKind.TypeX ? new Color(0.77f, 0.77f, 0.755f) : new Color(0.79f, 0.79f, 0.775f);
            return new DrywallProduct { Id = id, Name = name, Kind = k, LengthIn = len, ThickIn = t, Price = price, Face = face };
        }
        public static DrywallProduct Product(string id) => Products.Find(p => p.Id == id);

        public const string ScrewsId = "dw-screws", PaperTapeId = "dw-tape-paper", MeshTapeId = "dw-tape-mesh", BeadMetalId = "dw-bead-metal",
            BeadPaperId = "dw-bead-paper", SandpaperId = "dw-sandpaper", TextureMudId = "dw-mud-texture";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterCatalog()
        {
            var ids = new List<string>();
            foreach (var p in Products)
            {
                Catalog.Register(FinishStock.Item(p.Id, p.Name, p.Price, 1, p.Face, p.WidthIn, p.ThickIn, p.LengthIn));
                ids.Add(p.Id);
            }
            Catalog.Register(
                FinishStock.Item("dw-mud-ap", "All-purpose joint compound, 4.5 gal bucket", 19, 1, new Color(0.93f, 0.93f, 0.9f)),
                FinishStock.Item("dw-mud-set45", "Setting compound 45-min, 18 lb bag", 17, 1, new Color(0.9f, 0.88f, 0.84f)),
                FinishStock.Item(TextureMudId, "Spray texture compound, 4.5 gal", 21, 1, new Color(0.93f, 0.93f, 0.9f)),
                FinishStock.Item(ScrewsId, "Drywall screws 1-1/4\" coarse thread (5 lb, ~1000)", 32, 1000, new Color(0.12f, 0.12f, 0.13f)),
                FinishStock.Item(PaperTapeId, "Paper joint tape, 500' roll", 6, 500, new Color(0.92f, 0.9f, 0.84f)),
                FinishStock.Item(MeshTapeId, "Fiberglass mesh tape, 300' roll", 9, 300, new Color(0.95f, 0.95f, 0.85f)),
                FinishStock.Item(BeadMetalId, "Metal corner bead, 8'", 4, 1, new Color(0.7f, 0.72f, 0.74f)),
                FinishStock.Item(BeadPaperId, "Paper-faced metal corner bead, 8'", 6, 1, new Color(0.92f, 0.9f, 0.84f)),
                FinishStock.Item(SandpaperId, "Drywall sanding screens 120-grit (10 pk)", 9, 10, new Color(0.3f, 0.3f, 0.32f)));
            ids.AddRange(new[] { "dw-mud-ap", "dw-mud-set45", TextureMudId, ScrewsId, PaperTapeId, MeshTapeId, BeadMetalId, BeadPaperId, SandpaperId });
            FinishSupply.Add("Drywall", ids.ToArray());

            ToolFactory.Register(new ToolShopItem { Id = "drywall-tsquare", Name = "Drywall T-Square (48\")", Price = 25, Description = "Measure, score & snap wall sheets (needs the utility knife) and hang them: top row first, horizontal, butt ends on studs." }, () => new DrywallTSquare());
            ToolFactory.Register(new ToolShopItem { Id = "drywall-lift", Name = "Drywall Panel Lift", Price = 189, Description = "Raises 4x8 / 4x12 sheets to the ceiling. Ceilings are hung first, perpendicular to the joists." }, () => new DrywallLift());
            ToolFactory.Register(new ToolShopItem { Id = "screw-gun", Name = "Drywall Screw Gun (auto-feed)", Price = 229, Description = "Depth-set clutch dimples screws just below the paper. Hold Shift to auto-feed down a framing line." }, () => new ScrewGun());
            ToolFactory.Register(new ToolShopItem { Id = "drywall-router", Name = "Drywall Cut-out Router", Price = 89, Description = "Spiral bit follows the box: one pass per cut-out (boxes and openings)." }, () => new DrywallRouter());
            ToolFactory.Register(new ToolShopItem { Id = "keyhole-saw", Name = "Jab / Keyhole Saw", Price = 14, Description = "Hand saw for box cut-outs: plunge and saw (3 passes per box)." }, () => new KeyholeSaw());
            ToolFactory.Register(new ToolShopItem { Id = "taping-knives", Name = "Taping Knives 6/10/12\" + Mud Pan", Price = 58, Description = "6\" embeds tape (coat 1), 10\" second coat, 12\" third coat and skim. Spots screws. Sets corner bead. RMB = knife." }, () => new TapingKnives());
            ToolFactory.Register(new ToolShopItem { Id = "sanding-pole", Name = "Sanding Pole + Sponge", Price = 29, Description = "Pole sander for flats, sanding sponge for corners. After the third coat is dry." }, () => new SandingPole());
            ToolFactory.Register(new ToolShopItem { Id = "stilts", Name = "Drywall Stilts", Price = 159, Description = "Adjustable stilts: +18\" eye height and reach for ceilings and high walls. LMB put on / take off." }, () => new Stilts());

            Tablet.AddHelp("Drywall: ceiling first with the panel lift, then walls with the T-square (top row first, horizontal). Router/keyhole saw cut out boxes. Screw gun (Shift = auto-feed a stud). Knives: 6\" tape, 10\" coat 2, 12\" coat 3, each dry 24 h. Sand, texture, prime, paint.");
            FinishHooks.CoveringProviders.Add(CoveringAt);
        }

        /// <summary>FinishHooks provider: drywall thickness on a face at uv (0 where no sheet or in a cut-out).</summary>
        static float CoveringAt(FramePlane fp, Vector2 uv)
        {
            foreach (var s in Surfaces)
            {
                if (s == null || !s.Fp.SameFace(fp)) continue;
                var w = s.Fp.ToUV(fp.World(uv));
                foreach (var sh in s.Sheets)
                    if (sh.R.Contains(w) && !sh.InHole(w)) return sh.P.Thick;
            }
            return 0f;
        }
    }
}
