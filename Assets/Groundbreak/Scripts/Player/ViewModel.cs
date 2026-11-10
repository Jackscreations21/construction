using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The held tool in the lower right of the view. Real-size procedural models per tool family, drawn by a
    /// second camera (own layer, own 55° FOV, depth-cleared) so tools never clip into walls and don't stretch with the
    /// sprint FOV kick. Adds weapon-style sway (lags the mouse), gait bob, sprint tuck, equip raise and per-family use
    /// animations (hammer strike, nailer/drill recoil, roller/brush strokes). The renderer is only enabled while the
    /// view-model camera renders, so other cameras (test shots, overviews) never see a floating tool.</summary>
    [DefaultExecutionOrder(110)]
    public class ViewModel : MonoBehaviour
    {
        public const int Layer = 30;
        public static bool Enabled = true;

        enum Kind { None, Hammer, CircSaw, Drill, Nailer, Tape, Level, PryBar, Knife, Trowel, Roller, Brush, CaulkGun, LongHandle, Snips, Utility }
        enum Use { Swing, Recoil, Stroke, Poke }

        PlayerRig _rig; PlayerFeel _feel;
        Camera _vmCam;
        Transform _pivot; MeshFilter _mf; MeshRenderer _mr;
        Tool _shown; Kind _kind; Use _use;
        float _equipT = 1f, _useT = 1f, _heldT;
        Vector2 _swayPos, _swayVel, _lookLag;
        static Material[] _mats;

        public static string ShownKind { get; private set; } = "None";
        /// <summary>The camera that draws the held tool (render it after the player camera into the same target).</summary>
        public Camera Camera => _vmCam;
        /// <summary>Plays the current tool's use animation (tests, scripted demos). t01 = 0 starts it; other values freeze it there.</summary>
        public void SetUse(float t01) => _useT = Mathf.Clamp01(t01);
        public void SkipEquip() => _equipT = 1f;

        void Awake() { _rig = GetComponent<PlayerRig>(); _feel = GetComponent<PlayerFeel>(); }

        void Start()
        {
            if (_rig == null || _rig.Cam == null) return;
            _rig.Cam.cullingMask &= ~(1 << Layer);
            var camGo = new GameObject("ViewModelCamera") { layer = 2 };
            camGo.transform.SetParent(_rig.Cam.transform, false);
            _vmCam = camGo.AddComponent<Camera>();
            _vmCam.clearFlags = CameraClearFlags.Depth; _vmCam.cullingMask = 1 << Layer;
            _vmCam.depth = _rig.Cam.depth + 1; _vmCam.nearClipPlane = 0.01f; _vmCam.farClipPlane = 4f; _vmCam.fieldOfView = 55f;
            _vmCam.allowHDR = _rig.Cam.allowHDR; _vmCam.allowMSAA = _rig.Cam.allowMSAA;
            camGo.AddComponent<Gate>().Owner = this;

            var piv = new GameObject("ViewModel") { layer = Layer };
            piv.transform.SetParent(camGo.transform, false);
            _pivot = piv.transform;
            var model = new GameObject("Model") { layer = Layer };
            model.transform.SetParent(_pivot, false);
            _mf = model.AddComponent<MeshFilter>(); _mr = model.AddComponent<MeshRenderer>();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _mr.receiveShadows = false;
            _mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; _mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _mr.enabled = false;
        }

        void OnDestroy() { if (_mf != null && _mf.sharedMesh != null) Destroy(_mf.sharedMesh); }

        /// <summary>Only visible to the view-model camera.</summary>
        class Gate : MonoBehaviour
        {
            public ViewModel Owner;
            void OnPreCull() { if (Owner != null && Owner._mr != null) Owner._mr.enabled = Enabled && Owner._kind != Kind.None && Owner._mf.sharedMesh != null; }
            void OnPostRender() { if (Owner != null && Owner._mr != null) Owner._mr.enabled = false; }
        }

        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>Advances and applies the pose (dt = 0 re-applies the current pose, e.g. after SetUse).</summary>
        public void Step(float dt)
        {
            if (_pivot == null) return;
            var tool = Game.Belt != null ? Game.Belt.Active : null;
            if (tool != _shown) { _shown = tool; Rebuild(tool); _equipT = 0f; _useT = 1f; if (tool != null && _kind != Kind.None) Sfx.Play("equip", transform.position + Vector3.up, 0.5f); }

            bool live = !Game.UIOpen && Cursor.lockState == CursorLockMode.Locked;
            if (live && tool != null && _kind != Kind.None)
            {
                if (GInput.MouseDown(0)) { _useT = 0f; _heldT = 0f; }
                if (GInput.Mouse(0)) _heldT += dt;
                else _heldT = 0f;
            }
            _equipT = Mathf.Min(1f, _equipT + dt / 0.3f);
            _useT = Mathf.Min(1f, _useT + dt / UseDuration());

            // sway: spring toward the opposite of the mouse motion
            Vector2 md = live ? GInput.MouseDelta : Vector2.zero;
            Vector2 target = Vector2.ClampMagnitude(-md * 0.00045f, 0.035f);
            _swayVel += ((target - _swayPos) * 220f - _swayVel * 22f) * dt;
            _swayPos += _swayVel * dt;
            _lookLag = Vector2.Lerp(_lookLag, Vector2.ClampMagnitude(md * 0.12f, 6f), 1f - Mathf.Exp(-10f * dt));

            float bob = _feel != null ? _feel.BobBlend * PlayerFeel.BobScale : 0f, ph = _feel != null ? _feel.Phase : 0f;
            bool sprint = _feel != null && _feel.Sprinting;
            _sprintK = Mathf.MoveTowards(_sprintK, sprint ? 1f : 0f, dt * 4f);
            float breath = Mathf.Sin(Time.time * 1.6f) * 0.0025f;

            Pose(out var basePos, out var baseRot);
            Vector3 p = basePos
                      + new Vector3(_swayPos.x, _swayPos.y + breath, 0f)
                      + new Vector3(Mathf.Sin(ph) * 0.011f, -0.008f * (1f + Mathf.Cos(2f * ph)), 0f) * bob
                      + new Vector3(-0.03f, -0.05f, -0.02f) * _sprintK
                      + Vector3.up * ((_feel != null ? _feel.LandDip : 0f) * 0.5f);
            Quaternion r = Quaternion.Euler(-_lookLag.y, _lookLag.x, -_lookLag.x * 0.6f) * baseRot
                         * Quaternion.Euler(14f * _sprintK, -22f * _sprintK, 8f * _sprintK);

            // equip: raise from below the frame
            float e = 1f - Ease(_equipT);
            p += new Vector3(0.02f, -0.22f, -0.05f) * e; r = r * Quaternion.Euler(35f * e, 0f, 0f);

            // use animation
            UseOffset(out var up, out var ur);
            _pivot.localPosition = p + up;
            _pivot.localRotation = r * ur;
        }

        float _sprintK;

        static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        float UseDuration() => _use == Use.Swing ? 0.32f : _use == Use.Recoil ? 0.18f : _use == Use.Stroke ? 0.5f : 0.25f;

        void UseOffset(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            float t = _useT;
            switch (_use)
            {
                case Use.Swing:
                {
                    // quick wind-up (25%), strike (to 45%), slow return
                    float a = t < 0.25f ? -25f * Ease(t / 0.25f) : t < 0.45f ? Mathf.Lerp(-25f, 55f, (t - 0.25f) / 0.2f) : 55f * (1f - Ease((t - 0.45f) / 0.55f));
                    rot = Quaternion.Euler(a, 0f, -a * 0.15f);
                    pos = new Vector3(0f, -0.02f, 0.03f) * Mathf.Clamp01(a / 55f);
                    break;
                }
                case Use.Recoil:
                {
                    float k = t < 0.15f ? t / 0.15f : 1f - Ease((t - 0.15f) / 0.85f);
                    rot = Quaternion.Euler(-7f * k, 0f, 0f); pos = new Vector3(0f, 0.004f, -0.025f) * k;
                    break;
                }
                case Use.Stroke:
                {
                    // held: continuous up/down strokes; tap: one stroke
                    float s = _heldT > 0f ? Mathf.Sin(_heldT * 7f) : Mathf.Sin(t * Mathf.PI * 2f) * (1f - t);
                    pos = new Vector3(0f, s * 0.05f, 0.02f * Mathf.Abs(s)); rot = Quaternion.Euler(-s * 6f, 0f, 0f);
                    break;
                }
                default:
                {
                    float k = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    pos = new Vector3(0f, 0f, 0.05f) * k; rot = Quaternion.Euler(4f * k, 0f, 0f);
                    break;
                }
            }
            if (_use == Use.Recoil && _heldT > 0.2f && (_kind == Kind.Drill || _kind == Kind.CircSaw))
                pos += Random.insideUnitSphere * 0.0012f; // motor buzz while held
        }

        void Pose(out Vector3 pos, out Quaternion rot)
        {
            switch (_kind)
            {
                case Kind.Hammer: case Kind.PryBar: pos = new Vector3(0.2f, -0.24f, 0.36f); rot = Quaternion.Euler(-12f, -8f, -14f); break;
                case Kind.CircSaw: pos = new Vector3(0.16f, -0.22f, 0.42f); rot = Quaternion.Euler(4f, -6f, 0f); break;
                case Kind.Level: pos = new Vector3(0.22f, -0.25f, 0.42f); rot = Quaternion.Euler(0f, -22f, -4f); break;
                case Kind.LongHandle: pos = new Vector3(0.22f, -0.3f, 0.4f); rot = Quaternion.Euler(-35f, -10f, -10f); break;
                case Kind.Roller: pos = new Vector3(0.24f, -0.34f, 0.56f); rot = Quaternion.Euler(-18f, -12f, -6f); break;
                case Kind.Brush: pos = new Vector3(0.2f, -0.26f, 0.4f); rot = Quaternion.Euler(-20f, -10f, -6f); break;
                case Kind.Nailer: case Kind.Drill: case Kind.CaulkGun: pos = new Vector3(0.21f, -0.23f, 0.46f); rot = Quaternion.Euler(-2f, -6f, -3f); break;
                default: pos = new Vector3(0.19f, -0.2f, 0.36f); rot = Quaternion.Euler(-4f, -8f, -4f); break;
            }
        }

        // ------------------------------------------------------------------ models

        static Kind Classify(string id, out Use use)
        {
            use = Use.Poke;
            if (string.IsNullOrEmpty(id)) return Kind.None;
            switch (id)
            {
                case "hammer": case "staple-hammer": case "cap-stapler": use = Use.Swing; return Kind.Hammer;
                case "saw": use = Use.Recoil; return Kind.CircSaw;
                case "drill": case "fin-drill": case "ra-drill": case "screw-gun": case "drywall-router": use = Use.Recoil; return Kind.Drill;
                case "nailer": case "finish-nailer": case "brad-nailer": case "siding-nailer": case "flooring-nailer": case "staple-gun": use = Use.Recoil; return Kind.Nailer;
                case "tape": return Kind.Tape;
                case "level": return Kind.Level;
                case "prybar": use = Use.Swing; return Kind.PryBar;
                case "taping-knives": case "notched-trowel": case "grout-float": use = Use.Stroke; return Kind.Trowel;
                case "brick-trowel": use = Use.Swing; return Kind.Trowel;
                case "utility-knife": case "keyhole-saw": case "coping-saw": use = Use.Stroke; return Kind.Utility;
                case "paint-roller": case "sanding-pole": use = Use.Stroke; return Kind.Roller;
                case "paint-brush": use = Use.Stroke; return Kind.Brush;
                case "caulk-gun": case "foam-gun": use = Use.Poke; return Kind.CaulkGun;
                case "ext-snips": case "tubing-cutter": case "wire-strippers": case "lineman": return Kind.Snips;
                case "shovel": case "spade": case "rake": case "hoe": case "posthole": use = Use.Swing; return Kind.LongHandle;
            }
            return Kind.None;
        }

        // submeshes: 0 steel, 1 black rubber/plastic, 2 tool-body yellow, 3 wood, 4 white (tube, roller cover), 5 green (level vials)
        static Material[] Mats()
        {
            if (_mats != null && _mats[0] != null) return _mats;
            _mats = new[]
            {
                Materials.Get(SurfaceKind.BrushedSteel),
                Materials.Get(SurfaceKind.DarkPlastic),
                Materials.Get(SurfaceKind.Porcelain, new Color(0.98f, 0.70f, 0.06f)), // molded yellow tool housing (smooth, low-noise base)
                Materials.Get(SurfaceKind.Oak),
                Materials.Get(SurfaceKind.PaintedWhite),
                Materials.Get(SurfaceKind.Glass, new Color(0.45f, 0.95f, 0.35f)),
            };
            return _mats;
        }

        void Rebuild(Tool tool)
        {
            _kind = Classify(tool?.Id, out _use);
            ShownKind = _kind.ToString();
            if (_mf == null) return;
            if (_mf.sharedMesh != null) { Destroy(_mf.sharedMesh); _mf.sharedMesh = null; }
            if (_kind == Kind.None) return;
            var mb = new MeshBuilder { MinSubmeshCount = 6 };
            Build(mb, _kind, tool.Id);
            _mf.sharedMesh = mb.Build("VM_" + tool.Id);
            _mr.sharedMaterials = Mats();
        }

        static readonly Quaternion AlongX = Quaternion.Euler(0, 0, 90), AlongZ = Quaternion.Euler(90, 0, 0);

        /// <summary>Real-size tool models; origin at the grip, +Z away from the player, +Y up.</summary>
        static void Build(MeshBuilder m, Kind k, string id)
        {
            var I = Quaternion.identity;
            switch (k)
            {
                case Kind.Hammer: // 16 oz framing hammer: 13" handle, 4-1/2" head with rip claw
                    m.AddCylinder(new Vector3(0, -0.02f, 0), I, 0.016f, 0.13f, 12, 1);              // rubber grip
                    m.AddCylinder(new Vector3(0, 0.13f, 0), I, 0.012f, 0.2f, 10, id == "hammer" ? 2 : 1); // fiberglass shank
                    m.AddBevelBox(new Vector3(0, 0.245f, 0.012f), new Vector3(0.026f, 0.034f, 0.06f), I, 0.004f, 0);
                    m.AddCylinder(new Vector3(0, 0.245f, 0.055f), AlongZ, 0.015f, 0.03f, 14, 0);   // striking face
                    m.AddBox(new Vector3(0, 0.255f, -0.045f), new Vector3(0.02f, 0.012f, 0.07f), Quaternion.Euler(-22f, 0, 0), 0); // claw
                    break;
                case Kind.PryBar: // 24" flat bar
                    m.AddBox(new Vector3(0, 0.18f, 0), new Vector3(0.032f, 0.56f, 0.008f), I, 0, Vector3.up);
                    m.AddBox(new Vector3(0, 0.47f, 0.03f), new Vector3(0.03f, 0.008f, 0.07f), Quaternion.Euler(-30f, 0, 0), 0);
                    break;
                case Kind.CircSaw: // 7-1/4" worm-ish sidewinder
                    m.AddBox(new Vector3(0.02f, -0.07f, 0.06f), new Vector3(0.17f, 0.005f, 0.3f), I, 0);                 // shoe
                    m.AddCylinder(new Vector3(0.06f, 0.0f, 0.07f), AlongX, 0.105f, 0.03f, 28, 0);                        // upper guard
                    m.AddCylinder(new Vector3(-0.03f, 0.01f, 0.06f), AlongX, 0.048f, 0.11f, 18, 2);                      // motor
                    m.AddCylinder(new Vector3(-0.09f, 0.01f, 0.06f), AlongX, 0.045f, 0.03f, 18, 1);                      // end cap
                    m.AddRod(new Vector3(0.02f, 0.085f, 0.0f), Quaternion.Euler(-20f, 0, 0), 0.017f, 0.15f, 12, 1);    // top handle
                    m.AddBox(new Vector3(0.02f, 0.05f, -0.07f), new Vector3(0.03f, 0.07f, 0.03f), I, 2);
                    break;
                case Kind.Drill: // compact drill/driver, 20 V pack
                    m.AddBevelBox(new Vector3(0, 0.085f, 0.03f), new Vector3(0.055f, 0.07f, 0.17f), I, 0.012f, 2);
                    m.AddCylinder(new Vector3(0, 0.085f, 0.135f), AlongZ, 0.019f, 0.05f, 16, 1);                        // chuck
                    m.AddRod(new Vector3(0, 0.085f, 0.19f), I, id == "screw-gun" ? 0.0035f : 0.004f, 0.065f, 6, 0);     // bit
                    m.AddBox(new Vector3(0, 0.0f, -0.005f), new Vector3(0.04f, 0.12f, 0.05f), Quaternion.Euler(-12f, 0, 0), 1); // grip
                    m.AddBevelBox(new Vector3(0, -0.075f, 0.005f), new Vector3(0.07f, 0.045f, 0.11f), I, 0.006f, 1);    // battery
                    break;
                case Kind.Nailer:
                {
                    bool big = id == "nailer" || id == "siding-nailer" || id == "flooring-nailer";
                    float L = big ? 0.32f : 0.24f;
                    m.AddBevelBox(new Vector3(0, 0.095f, L * 0.3f), new Vector3(0.06f, 0.1f, L), I, 0.012f, 2);       // body/cylinder
                    m.AddBox(new Vector3(0, 0.04f, L * 0.78f), new Vector3(0.028f, 0.07f, 0.035f), I, 0);              // nose
                    m.AddBox(new Vector3(0, 0.01f, L * 0.42f), new Vector3(0.024f, 0.045f, L * 0.7f), Quaternion.Euler(-14f, 0, 0), 0); // magazine
                    m.AddBox(new Vector3(0, 0.0f, -0.02f), new Vector3(0.036f, 0.12f, 0.045f), Quaternion.Euler(-14f, 0, 0), 1);       // grip
                    break;
                }
                case Kind.Tape: // 25' tape
                    m.AddBevelBox(new Vector3(0, 0, 0), new Vector3(0.04f, 0.075f, 0.075f), I, 0.012f, 2);
                    m.AddBox(new Vector3(0, -0.03f, 0.075f), new Vector3(0.027f, 0.002f, 0.08f), I, 2);   // blade
                    m.AddBox(new Vector3(0, -0.026f, 0.116f), new Vector3(0.028f, 0.01f, 0.0015f), I, 0); // hook
                    m.AddBox(new Vector3(0.021f, 0.0f, 0.0f), new Vector3(0.003f, 0.04f, 0.04f), I, 1);
                    break;
                case Kind.Level: // 48" box-beam level
                    m.AddBox(new Vector3(0, 0, 0.35f), new Vector3(0.026f, 0.063f, 1.22f), I, 0, Vector3.forward);
                    m.AddBox(new Vector3(0, 0, 0.0f), new Vector3(0.028f, 0.025f, 0.06f), I, 5);
                    m.AddBox(new Vector3(0, 0, 0.62f), new Vector3(0.028f, 0.025f, 0.06f), I, 5);
                    m.AddBox(new Vector3(0, 0, -0.24f), new Vector3(0.03f, 0.068f, 0.03f), I, 1);
                    m.AddBox(new Vector3(0, 0, 0.94f), new Vector3(0.03f, 0.068f, 0.03f), I, 1);
                    break;
                case Kind.Trowel:
                {
                    float w = id == "taping-knives" ? 0.25f : id == "grout-float" ? 0.1f : 0.11f, l = id == "taping-knives" ? 0.07f : id == "brick-trowel" ? 0.25f : 0.28f;
                    m.AddCylinder(new Vector3(0, 0, 0), I, 0.016f, 0.12f, 12, id == "grout-float" ? 1 : 3);
                    if (id == "taping-knives") m.AddBox(new Vector3(0, 0.1f, 0.0f), new Vector3(w, l, 0.0015f), I, 0);
                    else
                    {
                        m.AddBox(new Vector3(0, 0.075f, 0.02f), new Vector3(0.012f, 0.03f, 0.01f), I, 0); // tang
                        m.AddBox(new Vector3(0, 0.09f, 0.03f + l * 0.4f), new Vector3(w, id == "grout-float" ? 0.02f : 0.002f, l), I, id == "grout-float" ? 1 : 0, Vector3.forward);
                    }
                    break;
                }
                case Kind.Utility:
                    if (id == "utility-knife")
                    {
                        m.AddBevelBox(new Vector3(0, 0, 0.03f), new Vector3(0.022f, 0.035f, 0.15f), I, 0.008f, 2);
                        m.AddBox(new Vector3(0, -0.006f, 0.115f), new Vector3(0.002f, 0.018f, 0.025f), Quaternion.Euler(20f, 0, 0), 0);
                    }
                    else
                    {
                        m.AddCylinder(new Vector3(0, 0, 0), I, 0.017f, 0.12f, 12, id == "coping-saw" ? 3 : 1);
                        if (id == "coping-saw") { m.AddBox(new Vector3(0, 0.12f, 0.06f), new Vector3(0.004f, 0.004f, 0.13f), I, 0); m.AddBox(new Vector3(0, 0.2f, 0.12f), new Vector3(0.004f, 0.16f, 0.004f), I, 0); m.AddBox(new Vector3(0, 0.28f, 0.06f), new Vector3(0.004f, 0.004f, 0.13f), I, 0); }
                        else m.AddBox(new Vector3(0, 0.13f, 0.0f), new Vector3(0.002f, 0.15f, 0.02f), I, 0);
                    }
                    break;
                case Kind.Roller: // 9" frame, 3/8" nap cover
                    m.AddCylinder(new Vector3(0, -0.02f, 0), I, 0.017f, 0.15f, 12, 1);
                    m.AddRod(new Vector3(0, 0.11f, 0.0f), Quaternion.Euler(-90, 0, 0), 0.003f, 0.12f, 6, 0);
                    m.AddRod(new Vector3(-0.06f, 0.17f, 0.0f), Quaternion.Euler(0, 90, 0), 0.003f, 0.12f, 6, 0);
                    if (id == "sanding-pole") m.AddBox(new Vector3(-0.12f, 0.19f, 0.0f), new Vector3(0.24f, 0.012f, 0.085f), I, 4);
                    else m.AddCylinder(new Vector3(-0.125f, 0.19f, 0.0f), AlongX, 0.03f, 0.23f, 20, 4);
                    break;
                case Kind.Brush: // 2-1/2" angled sash
                    m.AddCylinder(new Vector3(0, 0, 0), I, 0.008f, 0.17f, 10, 3);
                    m.AddBox(new Vector3(0, 0.1f, 0), new Vector3(0.064f, 0.035f, 0.014f), I, 0);
                    m.AddBox(new Vector3(0, 0.14f, 0), new Vector3(0.062f, 0.055f, 0.012f), Quaternion.Euler(0, 0, 0), 1);
                    break;
                case Kind.CaulkGun:
                    m.AddRod(new Vector3(0, 0.09f, 0.1f), I, 0.0045f, 0.3f, 6, 0);                                     // push rod
                    m.AddCylinder(new Vector3(0, 0.07f, 0.13f), AlongZ, 0.026f, 0.2f, 18, id == "foam-gun" ? 2 : 4); // tube / can
                    m.AddFrustum(new Vector3(0, 0.07f, 0.27f), AlongZ, 0.007f, 0.002f, 0.07f, 10, 4);                 // nozzle
                    m.AddBox(new Vector3(0, 0.07f, 0.03f), new Vector3(0.06f, 0.06f, 0.006f), I, 0);                   // plunger plate
                    m.AddBox(new Vector3(0, 0.0f, -0.01f), new Vector3(0.03f, 0.13f, 0.04f), Quaternion.Euler(-10f, 0, 0), 1);
                    m.AddBox(new Vector3(0, 0.0f, 0.04f), new Vector3(0.02f, 0.11f, 0.02f), Quaternion.Euler(10f, 0, 0), 0); // trigger
                    break;
                case Kind.Snips:
                    m.AddBox(new Vector3(-0.012f, -0.02f, -0.02f), new Vector3(0.016f, 0.11f, 0.02f), Quaternion.Euler(0, 0, 6f), id == "ext-snips" ? 2 : 1);
                    m.AddBox(new Vector3(0.012f, -0.02f, -0.02f), new Vector3(0.016f, 0.11f, 0.02f), Quaternion.Euler(0, 0, -6f), id == "ext-snips" ? 2 : 1);
                    m.AddBox(new Vector3(0, 0.07f, -0.02f), new Vector3(0.02f, 0.07f, 0.012f), I, 0);
                    break;
                case Kind.LongHandle: // 48" ash handle
                    m.AddCylinder(new Vector3(0, 0.25f, 0), I, 0.018f, 1.2f, 12, 3);
                    m.AddBox(new Vector3(0, -0.4f, 0.03f), new Vector3(0.22f, 0.28f, 0.004f), Quaternion.Euler(20f, 0, 0), 0);
                    break;
            }
        }
    }
}
