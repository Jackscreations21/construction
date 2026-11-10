using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Insulation trade: scripted API + registry of everything installed. Sequence on site:
    /// rough-ins done → air seal (foam / fire caulk) → insulate (batts, spray foam) → insulation inspection → drywall
    /// → blown attic over the hung ceiling.
    /// Typical scripted use (integration tests):
    /// <code>
    ///   var fp = Insulation.FaceFrom(pointInRoom, towardWall);    // or FramePlane.FromHit
    ///   Insulation.InsulateWall(fp, "ins-r13-k15");              // fit + split around obstacles + staple every bay
    ///   Insulation.AirSeal(fp);                                    // foam boxes, fire caulk plate penetrations
    ///   CloseupInspection.Run(CloseupInspection.Insulation);
    /// </code>
    /// </summary>
    public static partial class Insulation
    {
        public static readonly List<Batt> Batts = new List<Batt>();
        public static readonly List<FoamFill> Foams = new List<FoamFill>();
        public static readonly List<Vector3> SealedBoxes = new List<Vector3>();       // world centers of air-sealed boxes
        public static readonly List<Vector3> FireBlocked = new List<Vector3>();       // world points of fire-caulked penetrations
        public static readonly List<GameObject> Baffles = new List<GameObject>();
        public static readonly List<Vector3> Rulers = new List<Vector3>();
        public static readonly List<PolySheet> Poly = new List<PolySheet>();
        public static readonly List<AtticFill> Attics = new List<AtticFill>();
        /// <summary>Faces explicitly marked exterior (true) / interior (false). Others: exterior if sheathing backs the cavity.</summary>
        static readonly List<(FramePlane fp, bool ext)> _exterior = new List<(FramePlane, bool)>();
        static readonly Dictionary<string, float> _rollLeft = new Dictionary<string, float>();
        static int _seed = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            Batts.Clear(); Foams.Clear(); SealedBoxes.Clear(); FireBlocked.Clear(); Baffles.Clear(); Rulers.Clear(); Poly.Clear(); Attics.Clear();
            _exterior.Clear(); _rollLeft.Clear(); _shimmed.Clear();
        }

        // ================================================================ faces

        /// <summary>Framed face hit by a ray from <paramref name="from"/> along <paramref name="dir"/> (ignores finish layers).</summary>
        public static FramePlane FaceFrom(Vector3 from, Vector3 dir, float range = 6f)
        {
            foreach (var h in FinishAim.SortedHits(new Ray(from, dir.normalized), range))
            {
                if (h.collider.GetComponentInParent<FinishLayer>() != null) continue;
                var fp = FramePlane.FromHit(h);
                if (fp != null) return fp;
            }
            return null;
        }

        public static void MarkExterior(FramePlane fp, bool exterior = true)
        {
            _exterior.RemoveAll(e => e.fp.SameFace(fp));
            _exterior.Add((fp, exterior));
        }

        /// <summary>Exterior wall face = the cavity is backed by sheathing (or marked).</summary>
        public static bool IsExterior(FramePlane fp)
        {
            foreach (var e in _exterior) if (e.fp.SameFace(fp)) return e.ext;
            if (!fp.IsWall) return false;
            foreach (var b in fp.Bays())
            {
                if (b.IsOpening || b.R.height < 0.3f) continue;
                var start = fp.World(b.R.center, -0.01f);
                foreach (var h in Physics.RaycastAll(start, -fp.N, b.Depth + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var p = h.collider.GetComponentInParent<Piece>();
                    if (p != null && p.Def != null && p.Def.Category == PieceCategory.Sheet) return true;
                }
                return false;
            }
            return false;
        }

        // ================================================================ batts

        public static Batt BattAt(FramePlane fp, Vector2 uv)
        {
            foreach (var b in Batts) if (b != null && b.Fp.SameFace(fp) && b.R.Contains(uv)) return b;
            return null;
        }

        public static Batt BattIn(FramePlane fp, FramePlane.Bay bay) => BattAt(fp, bay.R.center);

        /// <summary>Score against a straightedge and push a batt into the bay, cut to the bay's height/width.
        /// Not yet split around boxes/pipes (that's <see cref="SplitAround"/>). Uses stock. Returns null with a toast on failure.</summary>
        public static Batt FitBatt(FramePlane fp, FramePlane.Bay bay, string productId, bool splitAround = false)
        {
            var p = Product(productId);
            if (p == null) { Game.Toast("Unknown batt " + productId); return null; }
            if (bay.IsOpening) { Game.Toast("That's a rough opening — no insulation there"); return null; }
            if (BattIn(fp, bay) != null || FoamIn(fp, bay) != null) { Game.Toast("This bay is already insulated"); return null; }
            float across = fp.IsWall ? bay.R.width : bay.R.width;
            if (p.Width < across - Units.In(0.25f)) Game.Toast($"{p.WidthIn:0}\" batt is narrower than this {Units.Format(across)} bay — gaps won't pass");
            // how many batts / how much roll
            float len = bay.R.height;
            int pieces = 1;
            if (p.Roll)
            {
                _rollLeft.TryGetValue(p.Id, out float left);
                while (left < len) { if (!FinishStock.Take(p.Id)) return null; left += p.Length; }
                _rollLeft[p.Id] = left - len;
            }
            else
            {
                pieces = Mathf.Max(1, Mathf.CeilToInt((len - Units.In(1f)) / p.Length));
                if (!FinishStock.Take(p.Id, pieces)) return null;
            }
            var go = new GameObject("Batt_" + p.Id);
            go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
            go.AddComponent<FinishLayer>().Kind = "batt";
            var b = go.AddComponent<Batt>();
            b.P = p; b.Fp = fp; b.R = bay.R; b.Depth = bay.Depth; b.Pieces = pieces; b.Seed = _seed++ * 17;
            b.Obstacles.AddRange(fp.Obstacles(bay.R, bay.Depth));
            b.CutAround = splitAround || b.Obstacles.Count == 0;
            go.GetComponent<FinishLayer>().Owner = b;
            b.Rebuild();
            Batts.Add(b);
            return b;
        }

        /// <summary>Split the batt in its thickness around pipes and notch it around boxes (no compression).</summary>
        public static bool SplitAround(Batt b)
        {
            if (b == null || b.CutAround) return false;
            b.CutAround = true; b.Rebuild();
            return true;
        }

        /// <summary>Staple a faced batt's flanges every 8" (face stapling over the stud faces, or inset to the stud sides).</summary>
        public static bool Staple(Batt b, bool inset = false)
        {
            if (b == null) return false;
            if (!b.P.Faced) { Game.Toast("Unfaced batts are friction-fit — nothing to staple"); return false; }
            if (b.Stapled) return false;
            int staples = Mathf.CeilToInt(b.R.height / Units.In(InsulationRules.StapleSpacingIn)) * 2;
            if (!FinishStock.Take(StaplesId, staples)) return false;
            b.Stapled = true; b.Inset = inset; b.Rebuild();
            return true;
        }

        /// <summary>Insulate every bay of a face: fit (cut to size), split around obstacles, staple faced flanges.</summary>
        public static int InsulateWall(FramePlane fp, string productId = "ins-r13-k15", bool split = true, bool staple = true)
        {
            int n = 0;
            foreach (var bay in fp.Bays())
            {
                if (bay.IsOpening || bay.R.height < Units.In(3f) || bay.R.width < Units.In(2f)) continue;
                var b = BattIn(fp, bay);
                if (b == null && FoamIn(fp, bay) != null) continue;
                if (b == null) b = FitBatt(fp, bay, productId, split);
                if (b == null) continue;
                if (split) SplitAround(b);
                if (staple && b.P.Faced) Staple(b);
                n++;
            }
            return n;
        }

        // ================================================================ spray foam

        public static FoamFill FoamIn(FramePlane fp, FramePlane.Bay bay)
        {
            foreach (var f in Foams) if (f != null && f.Fp.SameFace(fp) && f.R.Contains(bay.R.center)) return f;
            return null;
        }

        /// <summary>Spray one ~2" pass of closed-cell foam into the bay (stacks up to the cavity depth).</summary>
        public static FoamFill SprayFoam(FramePlane fp, FramePlane.Bay bay, float inches = 2f)
        {
            if (bay.IsOpening) { Game.Toast("That's a rough opening"); return null; }
            if (BattIn(fp, bay) != null) { Game.Toast("There's a batt in this bay"); return null; }
            var f = FoamIn(fp, bay);
            float add = Units.In(inches);
            if (f != null && f.Thick >= bay.Depth - 0.003f) { Game.Toast("Bay is full of foam"); return f; }
            int bdft = Mathf.CeilToInt(bay.R.width / Units.Foot * bay.R.height / Units.Foot * inches);
            if (!FinishStock.Take(FoamKitId, bdft)) return null;
            if (f == null)
            {
                var go = new GameObject("SprayFoam");
                go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
                go.AddComponent<FinishLayer>().Kind = "spray-foam";
                f = go.AddComponent<FoamFill>();
                f.Fp = fp; f.R = bay.R; f.Depth = bay.Depth; f.Seed = _seed++ * 31;
                Foams.Add(f);
            }
            f.Thick = Mathf.Min(bay.Depth, f.Thick + add);
            f.Rebuild();
            return f;
        }

        // ================================================================ air sealing & fire blocking

        /// <summary>Plate penetrations in a face: pipes/wires running through the top or bottom plate (world points at the plate).</summary>
        public static List<Vector3> Penetrations(FramePlane fp)
        {
            var res = new List<Vector3>();
            foreach (var bay in fp.Bays())
            {
                if (bay.IsOpening) continue;
                foreach (var o in fp.Obstacles(bay.R, bay.Depth))
                {
                    bool bottom = o.R.yMin < bay.R.yMin + 0.01f, top = o.R.yMax > bay.R.yMax - 0.01f;
                    float zc = -Mathf.Clamp(o.Proud - 0.02f, -bay.Depth, 0f);
                    float cz = Mathf.Min(bay.Depth * 0.5f, Mathf.Max(0.01f, -o.Proud + 0.01f));
                    if (bottom) res.Add(fp.World(o.R.center.x, bay.R.yMin, -cz));
                    if (top) res.Add(fp.World(o.R.center.x, bay.R.yMax, -cz));
                }
            }
            return res;
        }

        /// <summary>Electrical boxes in a face (obstacles whose front is at the face).</summary>
        public static List<(Vector3 center, FramePlane.Obstacle o)> Boxes(FramePlane fp)
        {
            var res = new List<(Vector3, FramePlane.Obstacle)>();
            foreach (var bay in fp.Bays())
            {
                if (bay.IsOpening) continue;
                foreach (var o in fp.Obstacles(bay.R, bay.Depth))
                    if (o.Proud > -0.006f) res.Add((fp.World(o.R.center, 0f), o));
            }
            return res;
        }

        static bool Near(List<Vector3> l, Vector3 p, float d = 0.08f) { foreach (var q in l) if ((q - p).sqrMagnitude < d * d) return true; return false; }
        public static bool IsBoxSealed(Vector3 c) => Near(SealedBoxes, c, 0.1f);
        public static bool IsFireBlocked(Vector3 p) => Near(FireBlocked, p, 0.1f);

        /// <summary>Foam bead around an electrical box (where it meets the stud and its wire knockouts).</summary>
        public static bool SealBox(FramePlane fp, FramePlane.Obstacle o)
        {
            var c = fp.World(o.R.center, 0f);
            if (IsBoxSealed(c)) return false;
            if (!FinishStock.Take(CanFoamId)) return false;
            var r = o.R; float m = Units.In(0.15f);
            var pts = new[] { new Vector2(r.xMin - m, r.yMin - m), new Vector2(r.xMax + m, r.yMin - m), new Vector2(r.xMax + m, r.yMax + m), new Vector2(r.xMin - m, r.yMax + m) };
            for (int i = 0; i < 4; i++) FoamBead(fp.World(pts[i], -0.009f), fp.World(pts[(i + 1) % 4], -0.009f), Units.In(0.45f), "foam-seal");
            SealedBoxes.Add(c);
            return true;
        }

        /// <summary>Re-shape batts on a face (e.g. pressed flat once drywall covers them).</summary>
        public static void RebuildBatts(FramePlane fp) { foreach (var b in Batts) if (b != null && b.Fp.SameFace(fp)) b.Rebuild(); }

        /// <summary>Lumpy can-foam bead (chain of irregular short beads).</summary>
        public static void FoamBead(Vector3 a, Vector3 b, float dia, string kind, bool lowExpansion = false)
        {
            float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.04f));
            var mat = FinishMaterials.Get(InsulationLooks.CanFoam);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)n); var q = Vector3.Lerp(a, b, (i + 1f) / n);
                float d = dia * (lowExpansion ? 0.8f : 1f) * (0.75f + 0.5f * Mathf.PerlinNoise(p.x * 30f + p.y * 7f, p.z * 30f));
                var go = Bead.Add(p, q + (q - p).normalized * d * 0.3f, new Color(0.95f, 0.88f, 0.62f), d, null, kind);
                if (go != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        /// <summary>Fire caulk (red) around a plate penetration.</summary>
        public static bool FireBlock(Vector3 p, FramePlane fp, CaulkGun gun = null)
        {
            if (IsFireBlocked(p)) return false;
            float r = Units.In(1.1f);
            if (gun != null) { if (!gun.Consume(2f * Mathf.PI * r)) return false; }
            else if (!FinishStock.Take(FireCaulkId)) return false;
            Vector3 ax1 = fp.U, ax2 = fp.N;
            var red = new Color(0.78f, 0.08f, 0.06f);
            for (int i = 0; i < 10; i++)
            {
                float a0 = i / 10f * Mathf.PI * 2f, a1 = (i + 1) / 10f * Mathf.PI * 2f;
                Bead.Add(p + (ax1 * Mathf.Cos(a0) + ax2 * Mathf.Sin(a0)) * r, p + (ax1 * Mathf.Cos(a1) + ax2 * Mathf.Sin(a1)) * r, red, Units.In(0.45f), null, "fire-caulk");
            }
            FireBlocked.Add(p);
            return true;
        }

        /// <summary>Caulk gun tube handler: fire-caulk the plate penetration nearest the aim.</summary>
        public static bool FireCaulkAt(RaycastHit hit, CaulkGun gun)
        {
            var fp = FaceAim.Front(hit);
            if (fp == null) { Game.Toast("Aim at a wall with pipes/wires through the plates"); return false; }
            Vector3 best = Vector3.zero; float bd = 0.5f; bool found = false;
            foreach (var p in Penetrations(fp))
            {
                float d = (p - hit.point).magnitude;
                if (d < bd && !IsFireBlocked(p)) { bd = d; best = p; found = true; }
            }
            if (!found) { Game.Toast("No open plate penetration near the aim"); return false; }
            bool ok = FireBlock(best, fp, gun);
            if (ok) Game.Toast("Fire caulk around the plate penetration");
            return ok;
        }

        /// <summary>Air-seal a face: foam every box, fire-caulk every plate penetration, foam window/door shim gaps.</summary>
        public static int AirSeal(FramePlane fp)
        {
            int n = 0;
            foreach (var (c, o) in Boxes(fp)) if (SealBox(fp, o)) n++;
            foreach (var p in Penetrations(fp)) if (FireBlock(p, fp)) n++;
            foreach (var r in fp.Openings()) if (FoamShimGaps(fp, r)) n++;
            return n;
        }

        static readonly List<Vector3> _shimmed = new List<Vector3>();
        /// <summary>Low-expansion window/door foam in the shim gap around a door/window that sits in the opening.</summary>
        public static bool FoamShimGaps(FramePlane fp, Rect opening)
        {
            var c = fp.World(opening.center, 0f);
            if (Near(_shimmed, c, 0.1f)) return false;
            var center = fp.World(opening.center, -0.05f);
            bool product = false;
            foreach (var col in Physics.OverlapBox(center, new Vector3(opening.width, opening.height, 0.2f) * 0.5f, fp.Rotation))
            {
                var p = col.GetComponentInParent<Piece>();
                if (p != null && p.Def != null && (p.Def.Category == PieceCategory.Door || p.Def.Category == PieceCategory.Window)) product = true;
            }
            if (!product) return false;
            if (!FinishStock.Take(CanFoamWwId)) return false;
            float z = -0.03f;
            FoamBead(fp.World(opening.xMin + 0.006f, opening.yMin, z), fp.World(opening.xMin + 0.006f, opening.yMax, z), Units.In(0.4f), "foam-shim", true);
            FoamBead(fp.World(opening.xMax - 0.006f, opening.yMin, z), fp.World(opening.xMax - 0.006f, opening.yMax, z), Units.In(0.4f), "foam-shim", true);
            FoamBead(fp.World(opening.xMin, opening.yMax - 0.006f, z), fp.World(opening.xMax, opening.yMax - 0.006f, z), Units.In(0.4f), "foam-shim", true);
            _shimmed.Add(c);
            return true;
        }

        // ================================================================ baffles, poly, rulers

        /// <summary>Exterior wall faces whose top plate this ceiling bay end rests on (eave bays).</summary>
        public static List<(FramePlane.Bay bay, Vector3 end)> EaveBays(FramePlane ceiling, List<FramePlane> exteriorWalls)
        {
            var res = new List<(FramePlane.Bay, Vector3)>();
            foreach (var bay in ceiling.Bays())
            {
                if (bay.IsOpening) continue;
                foreach (float v in new[] { bay.R.yMin, bay.R.yMax })
                {
                    var end = ceiling.World(bay.R.center.x, v, 0f);
                    foreach (var w in exteriorWalls)
                    {
                        // wall top plate under the ceiling at this end?
                        var uv = w.ToUV(end);
                        float behind = -w.ProudOf(end); // into the wall from its room face
                        if (uv.x > w.Bounds.xMin - 0.05f && uv.x < w.Bounds.xMax + 0.05f && behind > -InsulationRules.EaveBaffleReach && behind < 0.2f
                            && Mathf.Abs(uv.y - w.Bounds.yMax) < 0.25f)
                        { res.Add((bay, end)); break; }
                    }
                }
            }
            return res;
        }

        public static bool HasBaffle(Vector3 end) { foreach (var b in Baffles) if (b != null && (b.transform.position - end).sqrMagnitude < 0.2f * 0.2f) return true; return false; }

        /// <summary>Staple a corrugated foam eave baffle (22.5" x 48") in the ceiling bay at the exterior wall, keeping the
        /// soffit vent channel open above the insulation.</summary>
        public static GameObject InstallBaffle(FramePlane ceiling, FramePlane.Bay bay, Vector3 end)
        {
            if (HasBaffle(end)) { Game.Toast("Baffle already in this bay"); return null; }
            if (!FinishStock.Take(BaffleId)) return null;
            if (!FinishStock.Take(StaplesId, 6)) return null;
            // ceiling face: N = down; build in a frame on top of the joists: x across the bay, y along the joist away from the wall
            Vector3 up = -ceiling.N;
            var joistTop = ceiling.World(bay.R.center.x, 0f, -bay.Depth); // depth into framing = up
            float topY = Vector3.Dot(joistTop, up);
            Vector3 alongIn = (ceiling.World(bay.R.center, 0f) - end); alongIn = Vector3.ProjectOnPlane(alongIn, up).normalized;
            var go = new GameObject("EaveBaffle");
            go.AddComponent<FinishLayer>().Kind = "baffle";
            go.transform.rotation = Quaternion.LookRotation(alongIn, up) * Quaternion.Euler(-26.6f, 0f, 0f); // follows a 6/12 roof line up from the plate
            var sm = new SoftMesh();
            float w = Mathf.Min(Units.In(22.5f), bay.R.width + Units.In(1.5f)), L = Units.In(48f);
            // corrugated channel: profile across x with 4 troughs, extruded along z
            int seg = 32;
            for (int i = 0; i < seg; i++)
            {
                float x0 = -w / 2 + w * i / seg, x1 = -w / 2 + w * (i + 1) / seg;
                float y0 = Prof(x0, w), y1 = Prof(x1, w);
                sm.FlatQuad(new Vector3(x0, y0, 0), new Vector3(x1, y1, 0), new Vector3(x1, y1, L), new Vector3(x0, y0, L), 0, Vector3.down, Vector3.right, Vector3.forward);
                sm.FlatQuad(new Vector3(x0, y0 + 0.003f, 0), new Vector3(x1, y1 + 0.003f, 0), new Vector3(x1, y1 + 0.003f, L), new Vector3(x0, y0 + 0.003f, L), 0, Vector3.up, Vector3.right, Vector3.forward);
            }
            go.AddComponent<MeshFilter>().sharedMesh = sm.Build("Baffle");
            go.AddComponent<MeshRenderer>().sharedMaterial = FinishMaterials.Get(InsulationLooks.Baffle);
            go.transform.position = end + up * (topY - Vector3.Dot(end, up) + 0.01f);
            Baffles.Add(go);
            return go;
        }

        static float Prof(float x, float w)
        {
            // flat flanges at both sides (stapled to the joists' tops / roof deck), raised channel in the middle with ribs
            float t = Mathf.Abs(x) / (w * 0.5f);
            float channel = t < 0.85f ? Units.In(1.5f) : Mathf.Lerp(Units.In(1.5f), 0f, (t - 0.85f) / 0.15f);
            return channel + Mathf.Sin(x / w * Mathf.PI * 8f) * Units.In(0.2f);
        }

        /// <summary>6-mil poly vapor retarder stapled over the whole wall face (sheets lapped 6" at seams; cut at openings/boxes).</summary>
        public static PolySheet InstallPoly(FramePlane fp)
        {
            foreach (var p in Poly) if (p != null && p.Fp.SameFace(fp)) { Game.Toast("Poly already on this wall"); return p; }
            float area = fp.Bounds.width * fp.Bounds.height / (Units.Foot * Units.Foot);
            if (!FinishStock.Take(PolyId, Mathf.CeilToInt(area * 1.1f))) return null;
            if (!FinishStock.Take(StaplesId, Mathf.CeilToInt(area / 4f))) return null;
            var go = new GameObject("VaporPoly");
            go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
            go.AddComponent<FinishLayer>().Kind = "poly";
            var ps = go.AddComponent<PolySheet>();
            ps.Fp = fp;
            ps.Rebuild();
            Poly.Add(ps);
            return ps;
        }

        public static bool HasPoly(FramePlane fp) { foreach (var p in Poly) if (p != null && p.Fp.SameFace(fp)) return true; return false; }

        /// <summary>Staple a cardboard depth ruler to a joist (attic), facing the access.</summary>
        public static GameObject InstallRuler(Vector3 onJoistTop, Vector3 facing)
        {
            if (!FinishStock.Take(RulerId)) return null;
            var go = new GameObject("AtticRuler");
            go.AddComponent<FinishLayer>().Kind = "ruler";
            facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized; if (facing.sqrMagnitude < 0.1f) facing = Vector3.forward;
            go.transform.SetPositionAndRotation(onJoistTop, Quaternion.LookRotation(-facing, Vector3.up));
            var sm = new SoftMesh();
            float w = Units.In(2.5f), h = Units.In(24f);
            sm.FlatQuad(new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0), 0, Vector3.back, Vector3.right, Vector3.up);
            sm.FlatQuad(new Vector3(-w / 2, 0, 0.001f), new Vector3(w / 2, 0, 0.001f), new Vector3(w / 2, h, 0.001f), new Vector3(-w / 2, h, 0.001f), 0, Vector3.forward, Vector3.right, Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = sm.Build("Ruler");
            go.AddComponent<MeshRenderer>().sharedMaterial = FinishMaterials.Get(InsulationLooks.Ruler);
            Rulers.Add(onJoistTop);
            return go;
        }

        // ================================================================ attic

        /// <summary>Loose fill on the ceiling face (bottom of the joists). Created on first blow.</summary>
        public static AtticFill AtticFor(FramePlane ceiling, bool create)
        {
            foreach (var a in Attics) if (a != null && a.Fp.SameFace(ceiling)) return a;
            if (!create) return null;
            var go = new GameObject("AtticLooseFill");
            go.transform.SetPositionAndRotation(ceiling.Origin, ceiling.Rotation);
            go.AddComponent<FinishLayer>().Kind = "loose-fill";
            var af = go.AddComponent<AtticFill>();
            af.Init(ceiling);
            Attics.Add(af);
            return af;
        }

        /// <summary>Ceiling face (joist bottoms, N down) from any face of the joists.</summary>
        public static FramePlane CeilingFace(FramePlane any)
        {
            if (any == null) return null;
            if (any.IsCeiling) return any;
            if (any.IsFloor && any.Members.Count > 0) return FramePlane.FromPieceFace(any.Members[0].Piece, Vector3.down);
            return null;
        }

        /// <summary>Blow loose fill to a uniform target R over the whole attic (needs the ceiling drywall below).</summary>
        public static AtticFill BlowAttic(FramePlane ceiling, float targetR)
        {
            var cf = CeilingFace(ceiling);
            if (cf == null) return null;
            if (!AtticFill.CeilingBelow(cf)) { Game.Toast("Hang the ceiling drywall first — loose fill rests on it"); return null; }
            var af = AtticFor(cf, true);
            af.FillTo(Units.In(targetR / InsulationRules.LooseFillRPerInch) * 1.04f);
            return af;
        }

        /// <summary>Rulers needed for this attic: one per 300 sq ft (data).</summary>
        public static int RulersNeeded(FramePlane ceiling)
        {
            float sq = ceiling.Bounds.width * ceiling.Bounds.height / (Units.Foot * Units.Foot);
            return Mathf.Max(1, Mathf.CeilToInt(sq / InsulationRules.RulerEverySqFt));
        }

        public static int RulersIn(FramePlane ceiling)
        {
            int n = 0;
            var wb = ceiling.WorldBounds(); wb.Expand(new Vector3(0.1f, 1.2f, 0.1f));
            foreach (var r in Rulers) if (wb.Contains(r)) n++;
            return n;
        }

        /// <summary>Staple the required depth rulers on the attic joists, spread evenly.</summary>
        public static int InstallRulers(FramePlane ceiling)
        {
            var cf = CeilingFace(ceiling);
            int need = RulersNeeded(cf) - RulersIn(cf), n = 0;
            var lines = cf.FramingLinesU();
            for (int i = 0; i < need && lines.Count > 0; i++)
            {
                float u = lines[Mathf.Clamp((i + 1) * lines.Count / (need + 1), 0, lines.Count - 1)];
                float v = cf.Bounds.center.y + (i % 2 == 0 ? 0.3f : -0.3f);
                var m = cf.MemberAt(new Vector2(u, v));
                float depth = m != null ? m.Depth : Units.In(5.5f);
                var p = cf.World(u + Units.In(0.75f) + 0.002f, v, -depth + Units.In(0f));
                p.y = cf.World(u, v, 0f).y; // stand on the ceiling, against the joist side
                if (InstallRuler(p, cf.U) != null) n++;
            }
            return n;
        }

        /// <summary>Loose-fill bags for an R target over an area (bag label coverage: ~40 sq ft at R-38).</summary>
        public static int BagsFor(float sqft, float r) => Mathf.CeilToInt(sqft / (40f * 38f / Mathf.Max(1f, r)));
    }
}
