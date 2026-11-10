using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Electrical trade registration: NM-B / SER run specs, store catalog (cable, boxes, devices, breakers,
    /// consumables), MEP auto-attachers, tools, circuit simulator, tablet section and inspection rules.</summary>
    public static class ElCatalog
    {
        public const string SerId = "ser-40al";
        public const string WireNuts = "el-wirenuts", Staples = "el-staples";

        static float I(float inches) => Units.In(inches);

        static RunSpec Nm(string id, string name, int awg, int cond, float wIn, float hIn, Color tint, SurfaceKind surf, float amp, string stock, float ft, bool round = false)
        {
            return new RunSpec
            {
                Id = id, Name = name, Trade = Trade.Electrical, Kind = NetKind.Power,
                Shape = round ? RunShape.Round : RunShape.Flat, Width = I(wIn), Height = I(hIn), NominalIn = wIn,
                Surface = surf, Tint = tint, Join = JoinMethod.None, BendsInline = true, MinBendRadius = I(5f * hIn),
                StockId = stock, FeetPerUnit = ft, CutTool = "wire-strippers", FittingPrice = 0,
                Awg = awg, Conductors = cond, Ampacity = amp,
            };
        }

        public static IEnumerable<RunSpec> Specs()
        {
            yield return Nm("nm-14-2", "NM-B 14/2 w/G (white)", 14, 2, 0.40f, 0.21f, MepLook.RomexWhite, SurfaceKind.PaintedWhite, 15, "el-nm142-250", 250);
            yield return Nm("nm-12-2", "NM-B 12/2 w/G (yellow)", 12, 2, 0.46f, 0.24f, MepLook.RomexYellow, SurfaceKind.PaintedWhite, 20, "el-nm122-250", 250);
            yield return Nm("nm-14-3", "NM-B 14/3 w/G (white)", 14, 3, 0.53f, 0.27f, MepLook.RomexWhite, SurfaceKind.PaintedWhite, 15, "el-nm143-250", 250);
            yield return Nm("nm-12-3", "NM-B 12/3 w/G (yellow)", 12, 3, 0.60f, 0.31f, MepLook.RomexYellow, SurfaceKind.PaintedWhite, 20, "el-nm123-250", 250);
            yield return Nm("nm-10-3", "NM-B 10/3 w/G (orange)", 10, 3, 0.70f, 0.38f, MepLook.RomexOrange, SurfaceKind.PaintedWhite, 30, "el-nm103-125", 125);
            yield return Nm("nm-6-3", "NM-B 6/3 w/G (black)", 6, 3, 0.75f, 0.75f, Color.white, SurfaceKind.DarkPlastic, 55, "el-nm63-125", 125, true);
            var ser = Nm(SerId, "SER 4/0-4/0-4/0-2/0 Al service cable", 0, 3, 1.65f, 0.78f, new Color(0.62f, 0.63f, 0.65f), SurfaceKind.PaintedWhite, 200, "el-ser40al", 1);
            ser.NominalIn = 2f; ser.MinBendRadius = I(5f * 1.65f);
            yield return ser;
        }

        public static bool IsBranch(RunSpec s) => s != null && s.Trade == Trade.Electrical && s.Kind == NetKind.Power && s.Id != SerId;
        public static bool IsBranch(NetRun r) => r != null && IsBranch(r.Spec);

        // ------------------------------------------------------------------ breakers
        public enum BreakerType { Standard, Afci, Gfci, DualFunction }

        public struct BreakerDef { public string Id; public int Amps, Poles; public BreakerType Type; public string Name; }
        public static readonly List<BreakerDef> Breakers = new List<BreakerDef>();

        static string TypeName(BreakerType t) => t == BreakerType.Afci ? "AFCI" : t == BreakerType.Gfci ? "GFCI" : t == BreakerType.DualFunction ? "Dual-function AFCI/GFCI" : "";

        static void AddBreaker(int amps, int poles, BreakerType t, int price, List<PieceDefinition> defs)
        {
            string suf = t == BreakerType.Afci ? "-afci" : t == BreakerType.Gfci ? "-gfci" : t == BreakerType.DualFunction ? "-df" : "";
            string id = $"el-bkr-{amps}-{poles}p{suf}";
            string name = $"{amps}A {(poles == 2 ? "double-pole" : "single-pole")} {TypeName(t)} breaker".Replace("  ", " ");
            Breakers.Add(new BreakerDef { Id = id, Amps = amps, Poles = poles, Type = t, Name = name });
            defs.Add(D(id, name, 1f, poles == 2 ? 2f : 1f, 3f, price, new Color(0.1f, 0.1f, 0.1f)));
        }

        public static BreakerDef? Breaker(string id) { foreach (var b in Breakers) if (b.Id == id) return b; return null; }

        // ------------------------------------------------------------------ devices
        public static readonly (ElDevice dev, string id, string name)[] DeviceItems =
        {
            (ElDevice.Receptacle15, "el-dev-dup15", "Duplex receptacle 15A TR, white"),
            (ElDevice.Receptacle20, "el-dev-dup20", "Duplex receptacle 20A TR, white"),
            (ElDevice.Gfci, "el-dev-gfci", "GFCI receptacle 20A TR/WR, white"),
            (ElDevice.Switch, "el-dev-sp", "Single-pole switch 15A, white"),
            (ElDevice.Switch3, "el-dev-3w", "3-way switch 15A, white"),
        };
        public static string DeviceId(ElDevice d) { foreach (var x in DeviceItems) if (x.dev == d) return x.id; return null; }
        public static string DeviceName(ElDevice d) { foreach (var x in DeviceItems) if (x.dev == d) return x.name; return "none"; }

        static PieceDefinition D(string id, string name, float w, float h, float d, int price, Color c, PieceCategory cat = PieceCategory.Electrical, int units = 1) =>
            new PieceDefinition { Id = id, Name = name, Category = cat, Size = new Vector3(I(w), I(h), I(d)), Price = price, UnitsPerPurchase = units, Color = c };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            foreach (var s in Specs()) RunSpecs.Register(s);

            var white = new Color(0.95f, 0.95f, 0.93f);
            var defs = new List<PieceDefinition>
            {
                D("el-nm142-250", "Romex NM-B 14/2 w/G, 250' coil", 14, 6, 14, 98, white),
                D("el-nm122-250", "Romex NM-B 12/2 w/G, 250' coil", 15, 6, 15, 149, new Color(0.95f, 0.8f, 0.3f)),
                D("el-nm143-250", "Romex NM-B 14/3 w/G, 250' coil", 15, 7, 15, 159, white),
                D("el-nm123-250", "Romex NM-B 12/3 w/G, 250' coil", 16, 7, 16, 229, new Color(0.95f, 0.8f, 0.3f)),
                D("el-nm103-125", "Romex NM-B 10/3 w/G, 125' coil", 15, 6, 15, 209, new Color(0.95f, 0.5f, 0.2f)),
                D("el-nm63-125", "Romex NM-B 6/3 w/G, 125' coil", 18, 8, 18, 529, new Color(0.15f, 0.15f, 0.15f)),
                D("el-ser40al", "SER 4/0-4/0-4/0-2/0 Al service cable (per ft)", 2, 1, 12, 5, new Color(0.6f, 0.6f, 0.62f)),
                D("el-box-1g", "1-gang new-work box, blue PVC (20.3 cu in)", ElBox.W1g, ElBox.H1g, ElBox.D1g, 1, MepLook.BoxBlue),
                D("el-box-2g", "2-gang new-work box, blue PVC (32 cu in)", ElBox.W2g, ElBox.H1g, ElBox.D1g, 2, MepLook.BoxBlue),
                D("el-box-round", "4\" round ceiling box, blue PVC, nail-on (20 cu in)", ElBox.DRound, ElBox.HRound, ElBox.DRound, 3, MepLook.BoxBlue),
                D(WireNuts, "Wire nuts, assorted yellow/red/green (box of 100)", 3, 3, 3, 12, new Color(0.9f, 0.8f, 0.2f), PieceCategory.Consumable, 100),
                D(Staples, "Insulated cable staples 1/2\" (box of 250)", 3, 2, 3, 9, white, PieceCategory.Consumable, 250),
            };
            foreach (var d in DeviceItems)
            {
                int price = d.dev == ElDevice.Gfci ? 22 : d.dev == ElDevice.Receptacle20 ? 4 : d.dev == ElDevice.Switch3 ? 5 : 2;
                defs.Add(D(d.id, d.name, 1.3f, 4.2f, 1.3f, price, white));
            }
            Breakers.Clear();
            AddBreaker(15, 1, BreakerType.Standard, 9, defs);
            AddBreaker(20, 1, BreakerType.Standard, 9, defs);
            AddBreaker(30, 1, BreakerType.Standard, 12, defs);
            AddBreaker(15, 2, BreakerType.Standard, 22, defs);
            AddBreaker(20, 2, BreakerType.Standard, 22, defs);
            AddBreaker(30, 2, BreakerType.Standard, 24, defs);
            AddBreaker(40, 2, BreakerType.Standard, 26, defs);
            AddBreaker(50, 2, BreakerType.Standard, 29, defs);
            AddBreaker(15, 1, BreakerType.Afci, 45, defs);
            AddBreaker(20, 1, BreakerType.Afci, 46, defs);
            AddBreaker(15, 1, BreakerType.Gfci, 44, defs);
            AddBreaker(20, 1, BreakerType.Gfci, 46, defs);
            AddBreaker(30, 2, BreakerType.Gfci, 109, defs);
            AddBreaker(40, 2, BreakerType.Gfci, 115, defs);
            AddBreaker(50, 2, BreakerType.Gfci, 119, defs);
            AddBreaker(15, 1, BreakerType.DualFunction, 55, defs);
            AddBreaker(20, 1, BreakerType.DualFunction, 56, defs);
            Catalog.Register(defs.ToArray());

            // nodes
            MepAutoAttach.Register("el-box-1g", p => ElBox.Attach(p, ElBoxKind.OneGang));
            MepAutoAttach.Register("el-box-2g", p => ElBox.Attach(p, ElBoxKind.TwoGang));
            MepAutoAttach.Register("el-box-round", p => ElBox.Attach(p, ElBoxKind.Round));
            MepAutoAttach.Register("el-panel200", ElPanel.Attach);
            MepAutoAttach.Register("el-meter200", ElMeter.Attach);
            MepAutoAttach.Register("el-ceiling-light", p => ElLoad.Attach(p, 60f, true));
            MepAutoAttach.Register("el-ceiling-fan", p => ElLoad.Attach(p, 75f, true));
            MepAutoAttach.Register("el-porch-light", p => ElLoad.Attach(p, 40f, true));
            MepAutoAttach.Register("el-smoke", p => ElLoad.Attach(p, 1f, false));
            foreach (var s in Specs()) if (!string.IsNullOrEmpty(s.StockId)) { var id = s.StockId; MepAutoAttach.Register(id, ElVisuals.AttachCoil); }

            MepBootstrap.AddSystem(go => { go.AddComponent<ElSim>(); go.AddComponent<ElInteract>(); });
            ElTools.Register();
            ElTablet.Register();
            ElRules.Register();
        }
    }
}
