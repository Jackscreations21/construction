using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves the interior finish (save key "finish", loads after "drywall"): cabinets (each box: wall face, position, base / wall /
    /// filler, sink base, finish, hardware, shimmed or not, screws), countertops (type, which bases) and backsplashes, plank floors
    /// (LVP color / hardwood, area, holes, gap, layout seed) and tile floors (tile, stage reached: backer, thinset, set, grouted,
    /// haze wiped, with their times and grout color), hung interior doors (product, rough opening, swing, shimmed / plumb error)
    /// and every trim piece (baseboard, crown, casing: profile, species, run, end cuts, nails, painter's caulk with its beads).
    /// Everything is rebuilt with the trade's own builders (stock unlimited and the needed tools lent for the call: it was paid
    /// for), then set to the saved transform. Door units are left out of the plain piece list (each hung door makes its own).
    /// </summary>
    public static class FinishSave
    {
        public const string Key = "finish";

        [Serializable]
        class CabD
        {
            public SaveGaps.PlaneRef face = new SaveGaps.PlaneRef(); public float c, u, w, bottomY, levelErr; public bool isWall, filler, sink, shim, black;
            public int finish, wIn, screws, studs; public Vector3 pos; public Quaternion rot;
        }
        [Serializable] class CtD { public List<int> bases = new List<int>(); public int type; public Vector3 pos; public Quaternion rot; }
        [Serializable] class BsD { public int counter = -1; public string grout; public float height; }
        [Serializable]
        class FloorD
        {
            public bool tile; public int type; public SaveGaps.PlaneRef floor = new SaveGaps.PlaneRef(); public Rect area; public List<Rect> holes = new List<Rect>();
            public float gap; public string color; public int seed;
            public string tileId; public int step; public float thinsetAt, setAt, groutAt; public string grout;
        }
        [Serializable]
        class DoorD
        {
            public string def; public SaveGaps.PlaneRef face = new SaveGaps.PlaneRef(); public Rect ro; public bool towardFace, shim; public float plumbErr;
            public Vector3 pos; public Quaternion rot; public int jambNails;
        }
        [Serializable]
        class TrimD
        {
            public string profile; public int species, sc, ec, nails, inFraming, door = -1; public Vector3 origin, dir, outN, across;
            public float length, faceCover, reveal, stock; public bool caulked; public SaveGaps.PlaneRef face = new SaveGaps.PlaneRef();
        }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<CabD> cabinets = new List<CabD>(); public List<CtD> counters = new List<CtD>(); public List<BsD> splashes = new List<BsD>();
            public List<FloorD> floors = new List<FloorD>(); public List<DoorD> doors = new List<DoorD>(); public List<TrimD> trim = new List<TrimD>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, 30);
            SaveSystem.RegisterPieceFilter("hungdoor", p => p.GetComponentInParent<HungDoor>() != null || HungDoor.All.Exists(h => h != null && h.Piece == p));
        }

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            var cabs = new List<CabinetUnit>();
            foreach (var cu in CabinetUnit.All)
            {
                if (cu == null || !cu.gameObject.activeInHierarchy || cu.Face == null) continue;
                cabs.Add(cu);
                var fp = cu.Face; var pos = cu.transform.position;
                var mr = cu.GetComponentInChildren<MeshRenderer>();
                bool black = mr != null && mr.sharedMaterials.Length > 1 && mr.sharedMaterials[1] == Materials.Get(SurfaceKind.MatteBlack);
                d.cabinets.Add(new CabD
                {
                    face = SaveGaps.Plane(fp), c = fp.ProudOf(pos), u = fp.ToUV(pos).x - cu.Width, w = cu.Width, bottomY = cu.BottomY, levelErr = cu.LevelError,
                    isWall = cu.IsWall, filler = cu.IsFiller, sink = cu.IsSinkBase, shim = cu.Shimmed, black = black, finish = (int)cu.Finish, wIn = cu.WidthIn,
                    screws = cu.Screws, studs = cu.ScrewsInStuds, pos = pos, rot = cu.transform.rotation,
                });
            }
            var cts = new List<Countertop>();
            foreach (var ct in Countertop.All)
            {
                if (ct == null || !ct.gameObject.activeInHierarchy) continue;
                var cd = new CtD { type = (int)ct.Type, pos = ct.transform.position, rot = ct.transform.rotation };
                foreach (var b in ct.Bases) { int i = cabs.IndexOf(b); if (i >= 0) cd.bases.Add(i); }
                if (cd.bases.Count == 0) continue;
                cts.Add(ct); d.counters.Add(cd);
            }
            foreach (var bs in Backsplash.All)
            {
                if (bs == null || !bs.gameObject.activeInHierarchy) continue;
                int best = -1; float bd = 0.02f;
                for (int i = 0; i < cts.Count; i++)
                {
                    float dist = Vector3.Distance(cts[i].transform.position + Vector3.up * cts[i].Thick, bs.transform.position);
                    if (dist < bd) { bd = dist; best = i; }
                }
                if (best >= 0) d.splashes.Add(new BsD { counter = best, grout = bs.Grout, height = bs.Height });
            }
            foreach (var fi in UnityEngine.Object.FindObjectsByType<FloorInstall>(FindObjectsSortMode.InstanceID))
            {
                if (fi == null || !fi.gameObject.activeInHierarchy || fi.Floor == null) continue;
                var outer = Rect.MinMaxRect(fi.Area.xMin - fi.Gap, fi.Area.yMin - fi.Gap, fi.Area.xMax + fi.Gap, fi.Area.yMax + fi.Gap);
                var fd = new FloorD { type = (int)fi.Type, floor = SaveGaps.Plane(fi.Floor), area = outer, holes = new List<Rect>(fi.Holes), gap = fi.Gap, color = fi.LvpColor, seed = fi.Seed };
                var t = fi.GetComponent<TileFloor>();
                if (t != null) { fd.tile = true; fd.tileId = t.TileId; fd.step = (int)t.Step; fd.thinsetAt = t.ThinsetAt; fd.setAt = t.SetAt; fd.groutAt = t.GroutAt; fd.grout = t.GroutName; }
                else if (fi.Type == FloorType.Tile) continue;
                d.floors.Add(fd);
            }
            var doors = new List<HungDoor>();
            foreach (var hd in HungDoor.All)
            {
                if (hd == null || !hd.gameObject.activeInHierarchy || hd.FaceA == null) continue;
                doors.Add(hd);
                d.doors.Add(new DoorD
                {
                    def = hd.DefId, face = SaveGaps.Plane(hd.FaceA), ro = hd.RO, towardFace = Vector3.Dot(hd.Through, hd.FaceA.N) < 0f, shim = hd.Shimmed,
                    plumbErr = hd.PlumbError, pos = hd.transform.position, rot = hd.transform.rotation, jambNails = hd.JambNails,
                });
            }
            foreach (var t in TrimPiece.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy || t.Profile == null) continue;
                d.trim.Add(new TrimD
                {
                    profile = t.Profile.Id, species = (int)t.Species, sc = (int)t.StartCut, ec = (int)t.EndCut, nails = t.Nails, inFraming = t.NailsInFraming,
                    door = doors.IndexOf(t.Door), origin = t.Origin, dir = t.Dir, outN = t.Out, across = t.Across, length = t.Length, faceCover = t.FaceCover,
                    reveal = t.Reveal, stock = t.StockLength, caulked = t.Caulked, face = SaveGaps.Plane(t.Face),
                });
            }
            return JsonUtility.ToJson(d);
        }

        // ================================================================== load

        /// <summary>Removes the session's cabinets, tops, floors, doors and trim (what a quit loses; a load does this first).</summary>
        public static void Wipe()
        {
            foreach (var t in new List<TrimPiece>(TrimPiece.All)) if (t != null) SaveGaps.Kill(t.gameObject);
            foreach (var h in new List<HungDoor>(HungDoor.All)) if (h != null) { if (h.Piece != null) SaveGaps.Kill(h.Piece.gameObject); SaveGaps.Kill(h.gameObject); }
            foreach (var b in new List<Backsplash>(Backsplash.All)) if (b != null) SaveGaps.Kill(b.gameObject);
            foreach (var c in new List<Countertop>(Countertop.All)) if (c != null) SaveGaps.Kill(c.gameObject);
            foreach (var c in new List<CabinetUnit>(CabinetUnit.All)) if (c != null) SaveGaps.Kill(c.gameObject);
            foreach (var f in UnityEngine.Object.FindObjectsByType<FloorInstall>(FindObjectsSortMode.None)) if (f != null) SaveGaps.Kill(f.gameObject);
            TrimPiece.All.Clear(); HungDoor.All.Clear(); Backsplash.All.Clear(); Countertop.All.Clear(); CabinetUnit.All.Clear(); FloorInstall.All.Clear(); TileFloor.All.Clear();
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            Physics.SyncTransforms();
            int missing = 0;

            // floors first (cabinets sit on the subfloor / run over the floor's holes, trim reads the finished floor height)
            foreach (var fd in d.floors)
            {
                try
                {
                    var fp = SaveGaps.FindPlane(fd.floor);
                    if (fp == null) { missing++; continue; }
                    if (fd.tile) RestoreTile(fp, fd);
                    else if ((FloorType)fd.type == FloorType.Hardwood) SaveGaps.Replay(() => Hardwood.Install(fp, fd.area, fd.holes, fd.gap, fd.seed), "flooring-nailer", "finish-nailer");
                    else SaveGaps.Replay(() => Lvp.Install(fp, fd.area, fd.holes, string.IsNullOrEmpty(fd.color) ? "oak" : fd.color, fd.gap, fd.seed), "pull-bar");
                }
                catch (Exception e) { Debug.LogError("[Save] floor failed to restore: " + e); }
            }

            var cabs = new List<CabinetUnit>();
            foreach (var cd in d.cabinets)
            {
                CabinetUnit cu = null;
                try
                {
                    var fp = SaveGaps.FindPlane(cd.face);
                    if (fp == null) { missing++; cabs.Add(null); continue; }
                    cu = Cabinets.Rebuild(fp, cd.c, cd.u, cd.w, cd.bottomY, cd.isWall, (CabFinish)cd.finish, cd.filler, cd.sink, cd.shim, cd.black, cd.wIn);
                    cu.transform.SetPositionAndRotation(cd.pos, cd.rot);
                    cu.Screws = cd.screws; cu.ScrewsInStuds = cd.studs; cu.LevelError = cd.levelErr;
                }
                catch (Exception e) { Debug.LogError("[Save] cabinet failed to restore: " + e); }
                cabs.Add(cu);
            }
            Physics.SyncTransforms();
            var cts = new List<Countertop>();
            foreach (var cd in d.counters)
            {
                Countertop ct = null;
                try
                {
                    var run = new List<CabinetUnit>();
                    foreach (var i in cd.bases) if (i >= 0 && i < cabs.Count && cabs[i] != null) run.Add(cabs[i]);
                    if (run.Count > 0)
                    {
                        SaveGaps.Replay(() => ct = Countertops.Install(run, (CounterType)cd.type));
                        if (ct != null) ct.transform.SetPositionAndRotation(cd.pos, cd.rot);
                    }
                }
                catch (Exception e) { Debug.LogError("[Save] countertop failed to restore: " + e); }
                cts.Add(ct);
            }
            foreach (var bd in d.splashes)
            {
                var ct = bd.counter >= 0 && bd.counter < cts.Count ? cts[bd.counter] : null;
                if (ct == null) continue;
                SaveGaps.Replay(() => Countertops.InstallBacksplash(ct, bd.grout, bd.height), "tile-saw", "grout-float");
            }

            var doors = new List<HungDoor>();
            foreach (var dd in d.doors)
            {
                HungDoor hd = null;
                try
                {
                    var fp = SaveGaps.FindPlane(dd.face);
                    if (fp == null) { missing++; doors.Add(null); continue; }
                    SaveGaps.Replay(() => hd = DoorHanging.Hang(fp, dd.ro, dd.def, dd.towardFace, dd.shim), "finish-nailer", "level");
                    if (hd != null)
                    {
                        hd.PlumbError = dd.plumbErr; hd.JambNails = dd.jambNails;
                        hd.transform.SetPositionAndRotation(dd.pos, dd.rot);
                    }
                }
                catch (Exception e) { Debug.LogError("[Save] door failed to restore: " + e); }
                doors.Add(hd);
            }

            foreach (var td in d.trim)
            {
                try
                {
                    var prof = TrimProfile.Get(td.profile);
                    if (prof == null) continue;
                    var t = TrimPiece.Create(prof, (TrimSpecies)td.species, td.origin, td.dir, td.outN, td.across, td.length, (TrimCut)td.sc, (TrimCut)td.ec, SaveGaps.FindPlane(td.face));
                    t.FaceCover = td.faceCover; t.Reveal = td.reveal; t.StockLength = td.stock; t.Nails = td.nails; t.NailsInFraming = td.inFraming;
                    var door = td.door >= 0 && td.door < doors.Count ? doors[td.door] : null;
                    if (door != null) { t.Door = door; t.transform.SetParent(door.transform, true); door.Casings.Add(t); }
                    if (td.caulked) t.Caulk();
                }
                catch (Exception e) { Debug.LogError("[Save] trim piece failed to restore: " + e); }
            }
            if (missing > 0) Debug.LogWarning($"[Save] finish: {missing} item(s) had no matching framing face and were skipped");
        }

        static void RestoreTile(FramePlane fp, FloorD fd)
        {
            TileFloor t = null;
            var stage = (TileFloor.Stage)fd.step;
            SaveGaps.Replay(() =>
            {
                t = TileWork.Layout(fp, fd.area, fd.holes, string.IsNullOrEmpty(fd.tileId) ? "fin-tile-1224" : fd.tileId, fd.gap);
                if (t == null) return;
                if (stage >= TileFloor.Stage.Backer) TileWork.Backer(t);
                if (stage >= TileFloor.Stage.Thinset) TileWork.Trowel(t);
                if (stage >= TileFloor.Stage.Set) { t.ThinsetAt = FinishTime.Now; TileWork.Set(t); }
                if (stage >= TileFloor.Stage.Grouted) { t.SetAt = FinishTime.Now - TileWork.CureHours - 0.01f; TileWork.Grout(t, string.IsNullOrEmpty(fd.grout) ? "Delorean gray" : fd.grout); }
                if (stage >= TileFloor.Stage.Clean) { t.GroutAt = FinishTime.Now - TileWork.HazeHours - 0.01f; TileWork.WipeHaze(t); }
            }, "fin-drill", "notched-trowel", "tile-saw", "grout-float");
            if (t == null) return;
            t.ThinsetAt = fd.thinsetAt; t.SetAt = fd.setAt; t.GroutAt = fd.groutAt;
        }
    }
}
