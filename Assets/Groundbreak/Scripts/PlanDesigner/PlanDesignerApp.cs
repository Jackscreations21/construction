using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The "Plan Designer" tablet app (IMGUI). Start screen: blank lot / copy a library plan / My plans (edit, rename, delete,
    /// overlay, find a client). Editor: a blueprint canvas (front wall at the bottom, street below) with mouse tools, a side panel for
    /// room names / products / building systems, live numbers and the plan checker's findings, highlighted on the drawing.</summary>
    public static class PlanDesignerApp
    {
        public const string AppName = "Plan Designer";
        public enum DTool { Move, Footprint, Wall, Room, Name, Door, Window, Garage, Stair, Erase }
        static readonly string[] ToolLabels = { "Select / move wall", "Footprint", "Draw wall", "Room box", "Name room", "Door", "Window", "Garage door", "Stair", "Erase" };
        static readonly string[] ToolHelp =
        {
            "Click a room to select it (rename it on the right). Drag an inside wall to slide it.",
            "Drag the back-right corner of the building to size the footprint. The front-left corner stays put.",
            "Drag a line across rooms to split them with a wall (straight across or front to back).",
            "Drag a box to draw a room (pick its name on the right). It's cut out of whatever is there.",
            "Pick a name on the right, then click rooms to name them.",
            "Pick a door on the right, then click a wall. Outside doors on outside walls, inside doors between rooms.",
            "Pick a window on the right, then click an outside wall.",
            "Pick a garage/overhead door on the right, then click an outside wall of the garage or shop.",
            "Drag a box for the stair (it opens to the floor above; a second floor is added if needed).",
            "Click a door/window to remove it, a stair to delete it, or an inside wall to join the two rooms.",
        };

        // ---- state (the editor survives closing the tablet)
        public static DesignEditor Editor;
        public static int Floor;
        public static DTool Tool = DTool.Move;
        public static string Brush = "Bedroom", DoorProduct = "door-int-6p30", WindowProduct = "win-sh-3660", GarageProduct = "garage-8x7";
        static bool _start = true;
        static int _libGroup, _libSel, _issueFlash = -1; static float _flashUntil;
        static DesignRoom _selected; static string _roomText = "";
        static bool _dragging; static Vector2 _dragA, _dragB; static string _dragBase; static bool _wallDrag;
        static string _renameId, _renameText = "", _confirmDelete;
        static Rect _canvas; static Vector2 _origin; static float _scale = 10f;
        static GUIStyle _small, _tiny, _field, _bold, _issueStyle;
        static readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();

        static GUIStyle L => Tablet.I.LabelStyle; static GUIStyle Hd => Tablet.I.HeaderStyle; static GUIStyle B => Tablet.I.ButtonStyle;
        static float Fs => L.fontSize;
        static void Msg(string m) { if (!string.IsNullOrEmpty(m) && Tablet.I != null) Tablet.I.ShowMessage(m); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp(AppName, () => { if (Tablet.I != null) Draw(); });
            Tablet.SetAppGroup(AppName, "Plans");
            Tablet.AddHelp("Plan Designer app: draw your own house or building (footprint, walls, rooms, doors, windows, stairs), see its cost and code problems live, save it to My plans and build it like any plan. Ctrl+Z / Ctrl+Y undo/redo.");
            PlanDesigner.Changed += ClearThumbs;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Editor = null; Floor = 0; Tool = DTool.Move; _start = true; _selected = null; _dragging = false; _thumbs.Clear(); }

        static void ClearThumbs() { foreach (var t in _thumbs.Values) if (t != null) Object.Destroy(t); _thumbs.Clear(); }

        /// <summary>Opens a design in the editor (used by the start screen and tests).</summary>
        public static void Open(DesignEditor ed) { Editor = ed; Floor = 0; _start = false; _selected = null; Tool = DTool.Move; }

        static void Styles()
        {
            if (_small != null && _small.fontSize == Mathf.RoundToInt(Fs * 0.78f)) return;
            _small = new GUIStyle(L) { fontSize = Mathf.RoundToInt(Fs * 0.78f), alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            _tiny = new GUIStyle(_small) { fontSize = Mathf.RoundToInt(Fs * 0.66f) };
            _tiny.normal.textColor = new Color(0.85f, 0.92f, 1f);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(Fs * 0.95f) };
            _bold = new GUIStyle(L) { fontStyle = FontStyle.Bold };
            _issueStyle = new GUIStyle(B) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontSize = Mathf.RoundToInt(Fs * 0.85f) };
        }

        public static void Draw()
        {
            Styles();
            if (Editor == null || _start) { StartScreen(); return; }
            Keys();
            EditorScreen();
        }

        static void Keys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0 || !(e.control || e.command)) return;
            if (e.keyCode == KeyCode.Z) { if (e.shift) Editor.Redo(); else Editor.Undo(); Msg(Editor.CanUndo || Editor.CanRedo ? "Undo" : ""); e.Use(); }
            else if (e.keyCode == KeyCode.Y) { Editor.Redo(); e.Use(); }
        }

        // ================================================================== start screen

        static readonly string[] GroupNames = { "Residential", "Outbuilding", "Commercial", "Industrial" };

        static void StartScreen()
        {
            if (Editor != null && GUILayout.Button($"Back to the drawing: {Editor.Design.name}", B)) { _start = false; return; }
            GUILayout.Label("Start from a blank lot", Hd);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("House\n40' x 30'", B, GUILayout.Height(Fs * 3))) Open(DesignEditor.Blank(PlanGroup.Residential));
            if (GUILayout.Button("Garage / shop\n24' x 24'", B, GUILayout.Height(Fs * 3))) Open(DesignEditor.Blank(PlanGroup.Outbuilding));
            if (GUILayout.Button("Store / office\n50' x 60'", B, GUILayout.Height(Fs * 3))) Open(DesignEditor.Blank(PlanGroup.Commercial));
            if (GUILayout.Button("Warehouse\n60' x 80'", B, GUILayout.Height(Fs * 3))) Open(DesignEditor.Blank(PlanGroup.Industrial));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Copy a plan from the library and change it", Hd);
            _libGroup = GUILayout.Toolbar(_libGroup, GroupNames, B);
            var lib = new List<HousePlan>();
            foreach (var p in HousePlans.All) if (p.Group == (PlanGroup)_libGroup && !PlanDesigner.IsCustom(p)) lib.Add(p);
            if (lib.Count > 0)
            {
                var names = new string[lib.Count];
                for (int i = 0; i < lib.Count; i++) names[i] = lib[i].Name.Replace("The ", "") + $"  {lib[i].SqFt:N0}";
                _libSel = Mathf.Clamp(_libSel, 0, lib.Count - 1);
                _libSel = GUILayout.SelectionGrid(_libSel, names, 3, B);
                var sel = lib[_libSel];
                GUILayout.BeginHorizontal();
                GUILayout.Label(sel.Summary, L);
                if (GUILayout.Button("Copy & edit", B, GUILayout.Width(Fs * 8))) { Open(DesignEditor.CopyOf(sel)); Msg($"Copied {sel.Name}: change anything, then Save. The library plan stays as it was."); }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("My plans", Hd);
            if (PlanDesigner.Saved.Count == 0) GUILayout.Label("None yet. Saved plans show up here and in the Plans app under \"My plans\".", L);
            foreach (var d in new List<PlanDesign>(PlanDesigner.Saved)) MyPlanRow(d);
        }

        static Texture2D Thumb(PlanDesign d)
        {
            if (_thumbs.TryGetValue(d.id, out var t) && t != null) return t;
            var plan = (HousePlan)PlanDesigner.PlanOf(d.id) ?? d.Build();
            t = PlanPicture.Render(plan, 0, 128, 96);
            _thumbs[d.id] = t;
            return t;
        }

        static void MyPlanRow(PlanDesign d)
        {
            var plan = PlanDesigner.PlanOf(d.id);
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(Thumb(d), GUILayout.Width(128), GUILayout.Height(96));
            GUILayout.BeginVertical();
            if (_renameId == d.id)
            {
                GUILayout.BeginHorizontal();
                _renameText = GUILayout.TextField(_renameText, 40, _field, GUILayout.Width(Fs * 16));
                if (GUILayout.Button("OK", B, GUILayout.Width(Fs * 3))) { PlanDesigner.Rename(d.id, _renameText, out var m); Msg(m); _renameId = null; GUIUtility.keyboardControl = 0; }
                if (GUILayout.Button("Cancel", B, GUILayout.Width(Fs * 5))) { _renameId = null; GUIUtility.keyboardControl = 0; }
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label(d.name, _bold);
            var built = plan ?? d.Build();
            GUILayout.Label(built.Summary, L);
            GUILayout.Label(plan != null ? $"Ready to build · est. ${built.Takeoff.Total:N0}" + (PlanDesigner.InUse(d.id, out var w) ? $" · in use: {w}" : "")
                                         : "Draft: has problems, open it to fix them", L);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Edit", B)) { Open(new DesignEditor(d.Clone())); }
            if (GUILayout.Button("Rename", B)) { _renameId = d.id; _renameText = d.name; }
            if (_confirmDelete == d.id)
            {
                if (GUILayout.Button("Really delete?", B)) { PlanDesigner.Delete(d.id, out var m); Msg(m); _confirmDelete = null; }
            }
            else if (GUILayout.Button("Delete", B)) _confirmDelete = d.id;
            GUI.enabled = plan != null;
            if (GUILayout.Button("Show on my site", B)) { PlanDesigner.ShowOverlay(plan, 0, out var m); Msg(m); }
            if (GUILayout.Button("Find a client", B)) { PlanDesigner.OfferToClient(plan, out var m); Msg(m); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // ================================================================== editor

        static void EditorScreen()
        {
            var ed = Editor; var d = ed.Design;
            if (Floor >= d.floors) Floor = d.floors - 1;
            // row 1: file + floors
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◂ Plans", B, GUILayout.Width(Fs * 5))) { _start = true; return; }
            GUILayout.Label("Name", L, GUILayout.Width(Fs * 3));
            string nn = GUILayout.TextField(d.name, 40, _field, GUILayout.Width(Fs * 13));
            if (nn != d.name) { d.name = nn; ed.Changed(); }
            if (GUILayout.Button(string.IsNullOrEmpty(d.id) || PlanDesigner.GetDesign(d.id) == null ? "Save" : "Save changes", B)) { GUIUtility.keyboardControl = 0; PlanDesigner.Save(ed, false, out var m); Msg(m); }
            if (!string.IsNullOrEmpty(d.id) && PlanDesigner.GetDesign(d.id) != null && GUILayout.Button("Save as new", B)) { GUIUtility.keyboardControl = 0; PlanDesigner.Save(ed, true, out var m); Msg(m); }
            GUI.enabled = ed.CanUndo; if (GUILayout.Button("Undo", B, GUILayout.Width(Fs * 4))) { ed.Undo(); _selected = null; } GUI.enabled = ed.CanRedo;
            if (GUILayout.Button("Redo", B, GUILayout.Width(Fs * 4))) { ed.Redo(); _selected = null; } GUI.enabled = true;
            GUILayout.FlexibleSpace();
            for (int f = 0; f < d.floors; f++) if (GUILayout.Toggle(Floor == f, $"Floor {f + 1}", B, GUILayout.Width(Fs * 4.5f)) && Floor != f) { Floor = f; _selected = null; }
            if (d.floors < 3 && GUILayout.Button("+ Floor", B, GUILayout.Width(Fs * 4.5f))) { ed.AddFloor(); Floor = d.floors - 1; Msg(ed.Message); }
            if (d.floors > 1 && GUILayout.Button("− Floor", B, GUILayout.Width(Fs * 4.5f))) { ed.RemoveTopFloor(); Msg(ed.Message); }
            GUILayout.EndHorizontal();
            // row 2: tools
            int t = GUILayout.Toolbar((int)Tool, ToolLabels, B);
            if (t != (int)Tool) { Tool = (DTool)t; _dragging = false; }
            GUILayout.Label(ToolHelp[(int)Tool], _tiny);

            float cw = Mathf.Round(Tablet.I.ContentWidth * 0.63f), ch = Mathf.Round(Mathf.Min(cw * 0.78f, Screen.height * 0.5f));
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(cw));
            _canvas = GUILayoutUtility.GetRect(cw, ch, GUILayout.Width(cw), GUILayout.Height(ch));
            Canvas();
            GUILayout.Label(ed.StatsLine, _bold);
            Issues();
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            SidePanel();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ canvas

        static Vector2 ToScreen(Vector2 ft) => new Vector2(_origin.x + ft.x * _scale, _origin.y - ft.y * _scale);
        static Vector2 ToPlan(Vector2 s) => new Vector2((s.x - _origin.x) / _scale, (_origin.y - s.y) / _scale);
        static Rect ScreenRect(Rect ft) => new Rect(_origin.x + ft.xMin * _scale, _origin.y - ft.yMax * _scale, ft.width * _scale, ft.height * _scale);

        class GuiPainter : IPlanPainter
        {
            public float PxPerFt => _scale;
            public void Fill(Rect ft, Color c)
            {
                var r = ScreenRect(ft);
                if (!r.Overlaps(_canvas)) return;
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, _canvas.xMin), Mathf.Max(r.yMin, _canvas.yMin), Mathf.Min(r.xMax, _canvas.xMax), Mathf.Min(r.yMax, _canvas.yMax));
                GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
        }
        static readonly GuiPainter _painter = new GuiPainter();

        static void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); }

        static void Canvas()
        {
            var ed = Editor; var d = ed.Design; var plan = ed.Plan;
            // view: the footprint centered with room around it (more while sizing it)
            float margin = Tool == DTool.Footprint ? Mathf.Max(12f, Mathf.Max(d.width, d.depth) * 0.35f) : 7f;
            float vw = d.width + margin * 2f, vd = d.depth + margin * 2f;
            _scale = Mathf.Min(_canvas.width / vw, _canvas.height / vd);
            _origin = new Vector2(_canvas.x + (_canvas.width - d.width * _scale) / 2f, _canvas.yMax - (_canvas.height - d.depth * _scale) / 2f);
            if (Tool == DTool.Footprint) _origin = new Vector2(_canvas.x + margin * 0.5f * _scale, _canvas.yMax - margin * 0.6f * _scale);

            HandleMouse();
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            Box(_canvas, PlanPicture.Paper * 0.8f + new Color(0, 0, 0, 0.2f));
            if (plan != null)
            {
                var hi = new List<Rect>();
                var issues = ed.Issues;
                bool flash = _issueFlash >= 0 && Time.unscaledTime < _flashUntil;
                for (int i = 0; i < issues.Count; i++)
                {
                    var x = issues[i];
                    if (!x.Area.HasValue || (x.Floor >= 0 && x.Floor != Floor)) continue;
                    if (flash && i != _issueFlash) continue;
                    if (x.Error || (flash && i == _issueFlash)) hi.Add(x.Area.Value);
                }
                float k = flash ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 12f) : 1f;
                PlanPicture.Draw(_painter, plan, Floor, hi, _selected != null && _selected.floor == Floor ? _selected.Ft : (Rect?)null, o => ed.IsPlaced(o), k);
                RoomLabels(plan);
            }
            Dimensions(d);
            Preview();
            GUI.color = prev;
        }

        static void RoomLabels(CustomPlan plan)
        {
            GUI.color = Color.white;
            foreach (var r in plan.Rooms)
            {
                if (r.Floor != Floor) continue;
                var s = ScreenRect(r.Ft);
                if (s.width < 26f || s.height < 16f) continue;
                string dim = $"{DesignEditor.Ft(r.Ft.width)} x {DesignEditor.Ft(r.Ft.height)}";
                bool two = s.height > _small.fontSize * 2.6f;
                GUI.Label(new Rect(s.x, s.center.y - (two ? _small.fontSize : _small.fontSize * 0.6f), s.width, _small.fontSize * 1.3f), r.Name, _small);
                if (two && s.width > _tiny.fontSize * 7f) GUI.Label(new Rect(s.x, s.center.y + _small.fontSize * 0.25f, s.width, _tiny.fontSize * 1.3f), dim, _tiny);
            }
        }

        static void Dimensions(PlanDesign d)
        {
            GUI.color = new Color(1f, 0.9f, 0.55f);
            var a = ToScreen(new Vector2(0, 0)); var b = ToScreen(new Vector2(d.width, 0)); var c = ToScreen(new Vector2(0, d.depth));
            GUI.Label(new Rect(a.x, a.y + 4, b.x - a.x, _small.fontSize * 1.4f), "← " + DesignEditor.Ft(d.width) + " →", _small);
            GUI.Label(new Rect(a.x - _small.fontSize * 5.2f, c.y, _small.fontSize * 5f, a.y - c.y), DesignEditor.Ft(d.depth), _small);
            GUI.color = new Color(0.7f, 0.78f, 0.9f);
            GUI.Label(new Rect(a.x, a.y + _small.fontSize * 1.4f, b.x - a.x, _tiny.fontSize * 1.4f), "FRONT · street side", _tiny);
            // cursor readout
            var m = Event.current.mousePosition;
            if (_canvas.Contains(m))
            {
                var p = DesignEditor.Snap(ToPlan(m));
                GUI.color = Color.white;
                GUI.Label(new Rect(_canvas.x + 6, _canvas.y + 4, 360, _tiny.fontSize * 1.4f), $"{DesignEditor.Ft(p.x)} across, {DesignEditor.Ft(p.y)} back · grid {DesignEditor.Ft(DesignEditor.SnapFt)} (G)", _tiny);
                _tiny.alignment = TextAnchor.MiddleCenter;
            }
        }

        static void Preview()
        {
            if (!_dragging) return;
            var a = DesignEditor.Snap(_dragA); var b = DesignEditor.Snap(_dragB);
            var col = new Color(1f, 0.85f, 0.2f, 0.9f);
            switch (Tool)
            {
                case DTool.Room: case DTool.Stair:
                {
                    var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    var s = ScreenRect(r);
                    Box(s, new Color(1f, 0.85f, 0.2f, 0.25f));
                    Box(new Rect(s.x, s.y, s.width, 2), col); Box(new Rect(s.x, s.yMax - 2, s.width, 2), col); Box(new Rect(s.x, s.y, 2, s.height), col); Box(new Rect(s.xMax - 2, s.y, 2, s.height), col);
                    string lab = $"{DesignEditor.Ft(r.width)} x {DesignEditor.Ft(r.height)}";
                    if (Tool == DTool.Stair) lab += $"  (needs ≥ {DesignEditor.Ft(Editor.StairWidthFt)} x {DesignEditor.Ft(Editor.StairLengthFt)})";
                    else lab = (Brush ?? "Room") + " " + lab;
                    GUI.color = Color.white; GUI.Label(new Rect(s.x - 100, s.y - _small.fontSize * 1.5f, s.width + 200, _small.fontSize * 1.4f), lab, _small);
                    break;
                }
                case DTool.Wall:
                {
                    bool h = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
                    float c = DesignEditor.Snap(h ? (a.y + b.y) / 2f : (a.x + b.x) / 2f);
                    float lo = h ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y), hi = h ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
                    foreach (var r in Editor.WallHits(Floor, h, c, lo, Mathf.Max(hi, lo + 0.2f)))
                    {
                        var p0 = ToScreen(h ? new Vector2(r.x0, c) : new Vector2(c, r.y0)); var p1 = ToScreen(h ? new Vector2(r.x1, c) : new Vector2(c, r.y1));
                        Box(Rect.MinMaxRect(Mathf.Min(p0.x, p1.x) - 1.5f, Mathf.Min(p0.y, p1.y) - 1.5f, Mathf.Max(p0.x, p1.x) + 1.5f, Mathf.Max(p0.y, p1.y) + 1.5f), col);
                    }
                    var m = ToScreen(h ? new Vector2((lo + hi) / 2f, c) : new Vector2(c, (lo + hi) / 2f));
                    GUI.color = Color.white; GUI.Label(new Rect(m.x - 120, m.y - _small.fontSize * 1.6f, 240, _small.fontSize * 1.4f), $"wall at {DesignEditor.Ft(c)} {(h ? "back" : "across")}", _small);
                    break;
                }
                case DTool.Footprint:
                {
                    var s = ScreenRect(new Rect(0, 0, Editor.Design.width, Editor.Design.depth));
                    GUI.color = Color.white; GUI.Label(new Rect(s.x, s.y - _small.fontSize * 1.6f, s.width, _small.fontSize * 1.4f), $"{DesignEditor.Ft(Editor.Design.width)} x {DesignEditor.Ft(Editor.Design.depth)} = {Editor.Design.width * Editor.Design.depth:N0} sq ft per floor", _small);
                    break;
                }
            }
        }

        static void HandleMouse()
        {
            var e = Event.current; var ed = Editor;
            if (e.button != 0) return;
            var p = ToPlan(e.mousePosition);
            if (e.type == EventType.MouseDown && _canvas.Contains(e.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
                _dragA = _dragB = p; _dragging = false; _wallDrag = false;
                switch (Tool)
                {
                    case DTool.Move:
                        _selected = ed.RoomAt(Floor, p);
                        if (ed.FindWallLine(Floor, p, out _, out _, out _, out _) && NearEdge(p)) { _dragging = true; _wallDrag = true; _dragBase = ed.Design.ToJson(); ed.Push(); }
                        if (_selected != null) _roomText = _selected.name;
                        break;
                    case DTool.Footprint: _dragging = true; _dragBase = ed.Design.ToJson(); ed.Push(); break;
                    case DTool.Wall: case DTool.Room: case DTool.Stair: _dragging = true; break;
                    case DTool.Name: ed.NameRoom(Floor, p, Brush); Msg(ed.Message); break;
                    case DTool.Door: ed.PlaceOpening(Floor, p, DoorProduct); Msg(ed.Message); break;
                    case DTool.Window: ed.PlaceOpening(Floor, p, WindowProduct); Msg(ed.Message); break;
                    case DTool.Garage: ed.PlaceOpening(Floor, p, GarageProduct); Msg(ed.Message); break;
                    case DTool.Erase: ed.EraseAt(Floor, p); Msg(ed.Message); _selected = null; break;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _dragB = p;
                if (Tool == DTool.Move && _wallDrag)
                {
                    ed.Restore(_dragBase);
                    bool h; ed.FindWallLine(Floor, _dragA, out h, out _, out _, out _);
                    ed.MoveWall(Floor, _dragA, h ? p.y : p.x, false);
                    _selected = null;
                }
                else if (Tool == DTool.Footprint) { ed.Restore(_dragBase); ed.SetFootprint(p.x, p.y, false); }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = false; _dragB = p;
                switch (Tool)
                {
                    case DTool.Move:
                    case DTool.Footprint:
                        if (ed.Design.ToJson() == _dragBase) ed.DropUndo(); else Msg(ed.Message);
                        break;
                    case DTool.Wall: ed.DrawWall(Floor, _dragA, _dragB); Msg(ed.Message); break;
                    case DTool.Room: ed.AddRoom(Floor, Rect.MinMaxRect(Mathf.Min(_dragA.x, p.x), Mathf.Min(_dragA.y, p.y), Mathf.Max(_dragA.x, p.x), Mathf.Max(_dragA.y, p.y)), Brush); Msg(ed.Message); break;
                    case DTool.Stair:
                        if (ed.AddStair(Floor, Rect.MinMaxRect(Mathf.Min(_dragA.x, p.x), Mathf.Min(_dragA.y, p.y), Mathf.Max(_dragA.x, p.x), Mathf.Max(_dragA.y, p.y)))) { }
                        Msg(ed.Message); break;
                }
                e.Use();
            }
        }

        static bool NearEdge(Vector2 p)
        {
            float tol = Mathf.Max(0.6f, 7f / _scale);
            foreach (var r in Editor.Design.RoomsOn(Floor))
                if ((Mathf.Abs(p.x - r.x0) < tol || Mathf.Abs(p.x - r.x1) < tol) && p.y > r.y0 && p.y < r.y1 || (Mathf.Abs(p.y - r.y0) < tol || Mathf.Abs(p.y - r.y1) < tol) && p.x > r.x0 && p.x < r.x1) return true;
            return false;
        }

        // ------------------------------------------------------------------ issues

        static void Issues()
        {
            var issues = Editor.Issues;
            int errors = Editor.ErrorCount;
            GUILayout.Label(errors == 0 ? (issues.Count == 0 ? "✓ Plan checks out: ready to save and build." : "✓ No code problems. Tips:") : $"{errors} problem{(errors == 1 ? "" : "s")} to fix before it can be built (click one to see it):", _bold);
            int shown = 0;
            for (int i = 0; i < issues.Count && shown < 8; i++, shown++)
            {
                var x = issues[i];
                var c = GUI.color; GUI.color = x.Error ? new Color(1f, 0.6f, 0.55f) : new Color(0.85f, 0.9f, 1f);
                if (GUILayout.Button((x.Error ? "⚠ " : "• ") + x.Text, _issueStyle))
                {
                    if (x.Floor >= 0) Floor = x.Floor;
                    _issueFlash = i; _flashUntil = Time.unscaledTime + 2.5f;
                }
                GUI.color = c;
            }
            if (issues.Count > shown) GUILayout.Label($"… and {issues.Count - shown} more", _tiny);
        }

        // ------------------------------------------------------------------ side panel

        static readonly string[] ResRooms = { "Bedroom", "Master", "Bath", "Master Bath", "Powder", "Kitchen", "Living", "Family", "Dining", "Nook", "Hall", "Foyer", "Laundry", "Mud room", "Closet", "WIC", "Pantry", "Office", "Mech", "Garage", "Bonus room", "Open to below" };
        static readonly string[] OutRooms = { "Garage", "Shop", "Storage", "Loft", "Stall", "Barn aisle", "Tack room", "Bath", "Powder", "Office", "Mech", "Closet", "Hall" };
        static readonly string[] ComRooms = { "Sales floor", "Lobby", "Open office", "Office", "Conference", "Restroom", "Corridor", "Break room", "Storage", "Janitor", "Mech", "Cook line", "Dining", "Walk-in cooler", "Kitchen" };
        static readonly string[] IndRooms = { "Warehouse", "Shop", "Office", "Restroom", "Break room", "Corridor", "Storage", "Parts room", "Janitor", "Mech", "Lobby" };
        static readonly (string id, string label)[] ExtDoors = { ("door-ext-fg36", "Entry door 36\""), ("door-ext-steel36", "Steel entry 36\""), ("door-slider72", "Patio slider 6'"), ("door-ext-french72", "French doors 6'"), ("door-storefront36", "Storefront 36\""), ("door-storefront72", "Storefront pair 6'"), ("door-hm36", "Metal door 36\""), ("door-hm36-panic", "Exit door (panic bar)"), ("door-walk-36-steel", "Steel walk door") };
        static readonly (string id, string label)[] IntDoors = { ("door-int-6p30", "Interior 30\""), ("door-int-6p32", "Interior 32\""), ("door-int-6p24", "Interior 24\" (bath)"), ("door-int-sc36", "Interior 36\" (ADA)"), ("door-bifold36", "Bifold closet 36\""), ("door-pocket30", "Pocket door 30\""), ("door-fire20-36", "Fire door (garage)") };
        static readonly (string id, string label)[] Windows = { ("win-sh-3660", "36x60 (bedroom egress)"), ("win-sh-3648", "36x48"), ("win-sh-3636", "36x36"), ("win-sh-2436", "24x36 (bath)"), ("win-slider-4836", "48x36 slider (kitchen)"), ("win-picture-6048", "60x48 picture"), ("win-casement-2448", "24x48 casement"), ("win-storefront-7284", "Storefront 72x84") };
        static readonly (string id, string label)[] Overheads = { ("garage-8x7", "Garage door 8x7"), ("garage-18x8", "Garage door 18x8 (2 cars)"), ("oh-10x10", "Overhead 10x10"), ("oh-12x12", "Overhead 12x12"), ("oh-14x14", "Overhead 14x14"), ("oh-dock-9x10", "Dock door 9x10"), ("door-slide-12x12", "Barn slider 12x12") };

        static void Pick(ref string current, (string id, string label)[] list)
        {
            foreach (var (id, label) in list)
            {
                if (Catalog.Get(id) == null) continue;
                var def = Catalog.Get(id);
                if (GUILayout.Toggle(current == id, $"{label}  ${def.Price:N0}", B) && current != id) current = id;
            }
        }

        static void SidePanel()
        {
            var ed = Editor; var d = ed.Design;
            switch (Tool)
            {
                case DTool.Name: case DTool.Room:
                {
                    GUILayout.Label(Tool == DTool.Name ? "Click rooms to name them:" : "Room to draw:", _bold);
                    var list = d.Group == PlanGroup.Residential ? ResRooms : d.Group == PlanGroup.Outbuilding ? OutRooms : d.Group == PlanGroup.Commercial ? ComRooms : IndRooms;
                    int sel = System.Array.IndexOf(list, Brush);
                    int ns = GUILayout.SelectionGrid(sel, list, 2, B);
                    if (ns != sel && ns >= 0) Brush = list[ns];
                    break;
                }
                case DTool.Door:
                    GUILayout.Label("Outside doors:", _bold); Pick(ref DoorProduct, ExtDoors);
                    GUILayout.Label("Inside doors:", _bold); Pick(ref DoorProduct, IntDoors);
                    break;
                case DTool.Window: GUILayout.Label("Windows (outside walls):", _bold); Pick(ref WindowProduct, Windows); break;
                case DTool.Garage: GUILayout.Label("Vehicle doors:", _bold); Pick(ref GarageProduct, Overheads); break;
                case DTool.Stair:
                    float rise = ed.Plan != null ? ed.Plan.FloorToFloorIn(0) : 0f;
                    GUILayout.Label($"A full flight here needs about {DesignEditor.Ft(ed.StairWidthFt)} x {DesignEditor.Ft(ed.StairLengthFt)} (rise {rise:0}\", " +
                                    $"{(d.Group <= PlanGroup.Outbuilding ? "7 3/4\" max risers, 10\" treads" : "7\" max risers, 11\" treads")}). It must land in a hall or living area at both ends.", L);
                    break;
                case DTool.Footprint:
                {
                    GUILayout.Label("Footprint", _bold);
                    Stepper("Width", d.width, 1f, v => ed.SetFootprint(v, d.depth));
                    Stepper("Depth", d.depth, 1f, v => ed.SetFootprint(d.width, v));
                    break;
                }
                case DTool.Move:
                    if (_selected != null && ed.Design.rooms.Contains(_selected))
                    {
                        GUILayout.Label($"{_selected.name}: {DesignEditor.Ft(_selected.x1 - _selected.x0)} x {DesignEditor.Ft(_selected.y1 - _selected.y0)} ({(_selected.x1 - _selected.x0) * (_selected.y1 - _selected.y0):0} sq ft)", _bold);
                        GUILayout.BeginHorizontal();
                        _roomText = GUILayout.TextField(_roomText ?? "", 30, _field);
                        if (GUILayout.Button("Rename", B, GUILayout.Width(Fs * 5))) { ed.RenameRoom(_selected, _roomText); GUIUtility.keyboardControl = 0; Msg(ed.Message); }
                        GUILayout.EndHorizontal();
                        GUILayout.Label("Names decide what a room is: Bed…, Master, Bath, Powder, Kitchen, Living, Hall, Closet, Garage, Stair…", _tiny);
                    }
                    else GUILayout.Label("Click a room to select it.", L);
                    break;
                default: break;
            }
            GUILayout.Space(8);
            Building(ed, d);
        }

        static void Stepper(string label, float v, float step, System.Action<float> set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, L, GUILayout.Width(Fs * 6));
            if (GUILayout.Button("−4'", B, GUILayout.Width(Fs * 2.6f))) set(v - 4f * step);
            if (GUILayout.Button("−", B, GUILayout.Width(Fs * 2f))) set(v - step);
            GUILayout.Label(DesignEditor.Ft(v), _bold, GUILayout.Width(Fs * 4.5f));
            if (GUILayout.Button("+", B, GUILayout.Width(Fs * 2f))) set(v + step);
            if (GUILayout.Button("+4'", B, GUILayout.Width(Fs * 2.6f))) set(v + 4f * step);
            GUILayout.EndHorizontal();
        }

        static readonly string[] FoundNames = { "Slab", "Crawl", "Basement", "Posts", "Skids" };
        static readonly string[] RoofNames = { "Gable", "Hip", "Shed", "Flat", "Gambrel" };
        static readonly string[] CoverNames = { "Shingles", "Metal", "Standing seam", "TPO" };
        static readonly string[] FinishNames = { "Builder grade", "Standard", "Premium" };
        static readonly float[] FinishLevels = { 0.4f, 0.6f, 0.85f };

        static void Building(DesignEditor ed, PlanDesign d)
        {
            GUILayout.Label("Building", Hd);
            int g = GUILayout.Toolbar(d.group, GroupNames, B);
            if (g != d.group) { ed.Set(x => x.ApplyGroupDefaults((PlanGroup)g), $"Building type: {GroupNames[g]} (walls, roof and code rules changed to suit)."); Msg(ed.Message); }
            GUILayout.Label("Walls", _bold);
            int wp = GUILayout.Toolbar(d.WallPreset, PlanDesign.WallPresets, B);
            if (wp != d.WallPreset) { ed.Set(x => x.SetWallPreset(wp), "Walls: " + PlanDesign.WallPresets[wp]); Msg(ed.Message); }
            GUILayout.Label("Foundation", _bold);
            int fd = GUILayout.Toolbar(d.foundation, FoundNames, B);
            if (fd != d.foundation) { ed.Set(x => { x.foundation = fd; if (fd == (int)FoundationType.PostFrame) x.SetWallPreset(2); else if (x.WallFrame == WallSystem.PostFrame) x.SetWallPreset(0); }, "Foundation: " + (FoundationType)fd); Msg(ed.Message); }
            GUILayout.Label("Roof", _bold);
            int rf = GUILayout.Toolbar(d.roof, RoofNames, B);
            if (rf != d.roof) { ed.Set(x => x.SetRoof((RoofType)rf), "Roof: " + (RoofType)rf); Msg(ed.Message); }
            int cv = GUILayout.Toolbar(d.covering, CoverNames, B);
            if (cv != d.covering) { ed.Set(x => x.covering = cv, "Roofing: " + CoverNames[cv]); Msg(ed.Message); }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Pitch {d.pitch:0.##}/12", L, GUILayout.Width(Fs * 6));
            if (GUILayout.Button("−", B, GUILayout.Width(Fs * 2f))) ed.Set(x => x.pitch = Mathf.Max(0.25f, x.pitch >= 2f ? x.pitch - 1f : x.pitch - 0.25f), "Pitch lowered");
            if (GUILayout.Button("+", B, GUILayout.Width(Fs * 2f))) ed.Set(x => x.pitch = Mathf.Min(14f, x.pitch >= 1.75f ? x.pitch + 1f : x.pitch + 0.25f), "Pitch raised");
            GUILayout.Label($"Walls {DesignEditor.Ft(d.wallHeight)}", L, GUILayout.Width(Fs * 6));
            if (GUILayout.Button("−", B, GUILayout.Width(Fs * 2f))) ed.Set(x => x.wallHeight = Mathf.Max(7f, x.wallHeight - 1f), "Walls lowered");
            if (GUILayout.Button("+", B, GUILayout.Width(Fs * 2f))) ed.Set(x => x.wallHeight = Mathf.Min(40f, x.wallHeight + 1f), "Walls raised");
            GUILayout.EndHorizontal();
            int fi = d.quality < 0.5f ? 0 : d.quality < 0.75f ? 1 : 2;
            int nf = GUILayout.Toolbar(fi, FinishNames, B);
            if (nf != fi) ed.Set(x => x.quality = FinishLevels[nf], "Finish: " + FinishNames[nf]);
            GUILayout.BeginHorizontal();
            bool auto = GUILayout.Toggle(d.autoStructure, " Auto structure", GUILayout.Width(Fs * 8));
            if (auto != d.autoStructure) ed.Set(x => x.autoStructure = auto, auto ? "Joists and girders sized automatically" : "Structure as copied");
            GUILayout.Label($"{d.joist} joists, {d.bearingLines} girder line{(d.bearingLines == 1 ? "" : "s")}", _tiny);
            GUILayout.EndHorizontal();
            if (ed.Plan != null) GUILayout.Label($"{ed.Plan.Occupancy} · {ed.Plan.ConstructionType}", _tiny);
            // build it
            GUILayout.Space(6);
            GUILayout.Label("Build it", Hd);
            var reg = !string.IsNullOrEmpty(d.id) ? PlanDesigner.PlanOf(d.id) : null;
            if (reg == null) GUILayout.Label(ed.Valid ? "Save it to put it in My plans." : "Fix the problems, then save.", L);
            else
            {
                if (GUILayout.Button("Show it on my building site", B)) { PlanDesigner.ShowOverlay(reg, Floor, out var m); Msg(m); }
                if (GUILayout.Button("Find a client to build it for", B)) { PlanDesigner.OfferToClient(reg, out var m); Msg(m); }
                if (reg.Group == PlanGroup.Residential && reg.WidthFt <= 60f) GUILayout.Label("Also listed as a spec-house plan in the Development app.", _tiny);
            }
        }
    }
}
