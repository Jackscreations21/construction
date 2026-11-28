using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Shared helpers for the save sections that fill the save/load gaps (MEP networks, exterior work, set concrete slabs).
    /// Pieces are saved by <see cref="SaveSystem"/> and come back at the same position, so a feature that hangs state off a
    /// piece refers to it by <see cref="PieceRef"/> (definition id + world position) and finds the restored piece again.
    /// </summary>
    public static class SaveGaps
    {
        [Serializable] public class PieceRef { public string id; public Vector3 pos; }

        public static PieceRef Ref(Piece p) => p != null && p.Def != null ? new PieceRef { id = p.Def.Id, pos = p.transform.position } : null;

        /// <summary>Live pieces grouped by definition id, for many lookups in one load.</summary>
        public class PieceIndex
        {
            readonly Dictionary<string, List<Piece>> _byId = new Dictionary<string, List<Piece>>();

            public PieceIndex()
            {
                foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                {
                    if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) continue;
                    if (!_byId.TryGetValue(p.Def.Id, out var l)) _byId[p.Def.Id] = l = new List<Piece>();
                    l.Add(p);
                }
            }

            /// <summary>The live piece with this id nearest the saved position (within tol meters), or null.</summary>
            public Piece Find(PieceRef r, float tol = 0.01f)
            {
                if (r == null || string.IsNullOrEmpty(r.id) || !_byId.TryGetValue(r.id, out var l)) return null;
                Piece best = null; float bd = tol * tol;
                foreach (var p in l)
                {
                    if (p == null) continue;
                    float d = (p.transform.position - r.pos).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
                return best;
            }
        }

        /// <summary>Hides an object at once (no colliders / no list membership) and destroys it at the end of the frame.</summary>
        public static void Kill(GameObject g)
        {
            if (g == null) return;
            g.SetActive(false);
            UnityEngine.Object.Destroy(g);
        }

        /// <summary>Runs a rebuild step without leaving its toasts on screen (a load should not show "Deck: Joists done").</summary>
        public static void Quiet(Action a)
        {
            string t = Game.ToastText; float until = Game.ToastUntil;
            try { a(); }
            finally { Game.ToastText = t; Game.ToastUntil = until; }
        }
    }
}
