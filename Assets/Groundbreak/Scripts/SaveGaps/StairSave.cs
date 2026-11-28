using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves stair stringer work (save key "stairs"): every cut stringer (loose on the horses or nailed in place, from the framing
    /// square or the stair builder) comes back as the same sawtooth member with its <see cref="CutStringer"/> layout, state and
    /// nails, and every 2x12 with framing-square pencil layout on it comes back laid out (<see cref="StringerLayoutMark"/>) so it
    /// can still be cut. Cut stringers are left out of the plain piece list (they would come back as plain 2x12s).
    /// </summary>
    public static class StairSave
    {
        public const string Key = "stairs";

        [Serializable]
        class CutD
        {
            public string id, group; public Vector3 size, pos; public Quaternion rot; public int state, nails; public bool builder;
            public StairResult result;
        }
        [Serializable] class MarkD { public SaveGaps.PieceRef board = new SaveGaps.PieceRef(); public StairResult result; }
        [Serializable] class Data { public int version = 1; public List<CutD> cut = new List<CutD>(); public List<MarkD> marks = new List<MarkD>(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, 5);
            SaveSystem.RegisterPieceFilter("cutstringer", p => p.GetComponent<CutStringer>() != null && !ShellSave.IsShellPiece(p));
        }

        static float M(float inches) => inches * Units.Inch;

        static string Save()
        {
            var d = new Data();
            foreach (var cs in UnityEngine.Object.FindObjectsByType<CutStringer>(FindObjectsSortMode.InstanceID))
            {
                var p = cs != null ? cs.GetComponent<Piece>() : null;
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy || ShellSave.IsShellPiece(p)) continue;
                var m = p.GetComponent<StructuralMember>();
                d.cut.Add(new CutD
                {
                    id = p.Def.Id, group = m != null ? m.Group : "", size = p.Size, pos = p.transform.position, rot = p.transform.rotation, state = (int)p.State, nails = p.Nails,
                    builder = p.transform.Find("SeatContact 1") != null, result = cs.Result,
                });
            }
            foreach (var mk in UnityEngine.Object.FindObjectsByType<StringerLayoutMark>(FindObjectsSortMode.InstanceID))
            {
                var p = mk != null ? mk.GetComponent<Piece>() : null;
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy || p.GetComponent<CutStringer>() != null) continue;
                d.marks.Add(new MarkD { board = SaveGaps.Ref(p), result = mk.Result });
            }
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            // cut stringers from the session being left are pieces (cleared with the rest); nothing else to wipe
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            foreach (var cd in d.cut)
            {
                try { Rebuild(cd); }
                catch (Exception e) { Debug.LogError("[Save] cut stringer failed to restore: " + e); }
            }
            var pieces = new SaveGaps.PieceIndex();
            foreach (var md in d.marks)
            {
                var p = pieces.Find(md.board);
                if (p == null) continue;
                try { StringerMesh.LayOutOnBoard(p, md.result, out _); }
                catch (Exception e) { Debug.LogError("[Save] stringer layout failed to restore: " + e); }
            }
        }

        static void Rebuild(CutD cd)
        {
            var def = Catalog.Get(cd.id);
            if (def == null) return;
            var r = cd.result;
            var lay = new StringerLayout(r);
            float uC, xc = 0f, zc = 0f;
            if (cd.builder) BuilderFrame(lay, out uC, out xc, out zc);
            else uC = lay.UMin + Units.ToInches(cd.size.z) * 0.5f - 0.25f;   // framing square: layout 1/4" in from the -Z end
            Vector3 Local(Vector2 p) { var q = lay.ToBoard(p, uC); return new Vector3(M(q.x - xc), 0f, M(q.y - zc)); }
            var size = cd.size;
            var m = Framer.Custom(def.Id, size, null, cd.pos, cd.rot, MemberRole.Stringer,
                (b, s) => StringerMesh.Solid(b, lay, Local, Vector3.up, size.y, Vector3.forward, s(Materials.ForDefinition(def)), s(SurfaceKind.PineLumber)),
                Connector.Nailed, cd.group ?? "");
            var p = m.Piece;
            StringerMesh.RemoveStockVisuals(p);
            p.transform.SetPositionAndRotation(cd.pos, cd.rot);
            p.State = (PieceState)cd.state; p.Nails = cd.nails;
            p.gameObject.name = (cd.builder ? "Stringer (" : "Cut stringer (") + def.Name + ")";
            p.gameObject.AddComponent<CutStringer>().Result = r;
        }

        /// <summary>Board frame of a stair-builder stringer (same as <see cref="StairBuilder.MakeStringer"/>): layout center and
        /// the collider box center in board coordinates.</summary>
        static void BuilderFrame(StringerLayout lay, out float uC, out float xc, out float zc)
        {
            var r = lay.R;
            float W = r.BoardWidthIn;
            uC = (lay.UMin + lay.UMax) * 0.5f;
            float xLo, xHi, zLo, zHi;
            if (lay.FootOnBottomEdge)
            {
                xLo = -W * 0.5f; xHi = -W * 0.5f + r.ThroatIn;
                zLo = Vector2.Dot(new Vector2(lay.X0, 0f), lay.U) - uC;
                zHi = Vector2.Dot(new Vector2(lay.XTop, lay.BottomEdgeY(lay.XTop)), lay.U) - uC;
            }
            else
            {
                xLo = zLo = float.MaxValue; xHi = zHi = float.MinValue;
                foreach (var p in lay.Profile)
                {
                    var q = lay.ToBoard(p, uC);
                    xLo = Mathf.Min(xLo, q.x); xHi = Mathf.Max(xHi, q.x); zLo = Mathf.Min(zLo, q.y); zHi = Mathf.Max(zHi, q.y);
                }
            }
            xc = (xLo + xHi) * 0.5f; zc = (zLo + zHi) * 0.5f;
        }
    }
}
