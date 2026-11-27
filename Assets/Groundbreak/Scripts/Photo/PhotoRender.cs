using UnityEngine;

namespace Groundbreak
{
    /// <summary>Everything a photo needs besides the camera pose: lens, exposure, filter, frame, flash.</summary>
    [System.Serializable]
    public class PhotoSettings
    {
        public float Fov = 60f;            // vertical field of view, degrees
        public float Roll;                 // degrees, + = clockwise
        public bool Dof;                   // depth of field (lens-aperture sampling)
        public float FStop = 2.8f;         // aperture
        public float Focus = 5f;           // focus distance, m
        public float Exposure = 1f;
        public float Vignette = 0.25f;
        public int Filter;                 // index into PhotoRender.Filters
        public int Frame;                  // index into PhotoRender.Frames
        public bool Flash;                 // small light at the camera (interiors)

        public PhotoSettings Clone() => (PhotoSettings)MemberwiseClone();

        /// <summary>Lens focal length (mm) for this field of view on a full-frame (24 mm tall) sensor.</summary>
        public float FocalMm => 12f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>Renders photos in the built-in pipeline: a hidden camera drawn into RenderTextures, real depth of field by
    /// averaging views from points across the lens aperture (the focal plane stays sharp), then the colour filter,
    /// vignette and frame from Hidden/Groundbreak/Photo. Also writes PNGs.</summary>
    public static class PhotoRender
    {
        public static readonly string[] Filters = { "Natural", "Black & white", "Sepia", "Warm", "Cool", "Vivid", "Faded film" };
        public static readonly string[] Frames = { "None", "Instant print", "Gallery mat", "Cinema 2.39:1", "Site corners" };
        public const int ViewModelLayer = 30;     // first-person tool model (ViewModel.Layer): never in photos
        public const int DofSamplesCapture = 32;

        static Camera _cam;
        static Material _mat;
        static Light _flash;

        public static Material Mat
        {
            get
            {
                if (_mat == null)
                {
                    var sh = Resources.Load<Shader>("GroundbreakPhoto");
                    if (sh == null) sh = Shader.Find("Hidden/Groundbreak/Photo");
                    if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                }
                return _mat;
            }
        }

        public static Camera Cam
        {
            get
            {
                if (_cam == null)
                {
                    var go = new GameObject("PhotoCamera") { hideFlags = HideFlags.DontSave };
                    Object.DontDestroyOnLoad(go);
                    _cam = go.AddComponent<Camera>();
                    _cam.enabled = false;
                    _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 1500f;
                    _cam.cullingMask = ~(1 << ViewModelLayer);
                    _cam.allowMSAA = true;
                    var src = Game.Player != null ? Game.Player.Cam : Camera.main;
                    if (src != null) { _cam.clearFlags = src.clearFlags; _cam.backgroundColor = src.backgroundColor; }
                    else _cam.clearFlags = CameraClearFlags.Skybox;
                    var lg = new GameObject("PhotoFlash");
                    lg.transform.SetParent(go.transform, false);
                    lg.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                    _flash = lg.AddComponent<Light>();
                    _flash.type = LightType.Point; _flash.range = 9f; _flash.intensity = 1.4f; _flash.color = new Color(1f, 0.97f, 0.92f);
                    _flash.shadows = LightShadows.None; _flash.enabled = false;
                }
                return _cam;
            }
        }

        /// <summary>A point inside the aperture for sample i of n (golden-angle spiral, evenly fills the disk).</summary>
        public static Vector2 LensSample(int i, int n)
        {
            if (n <= 1) return Vector2.zero;
            float r = Mathf.Sqrt((i + 0.5f) / n), a = i * 2.39996323f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>Renders one view (lens sample) into rt.</summary>
        public static void RenderSample(Vector3 pos, Quaternion rot, PhotoSettings s, RenderTexture rt, Vector2 lens)
        {
            var cam = Cam;
            cam.fieldOfView = s.Fov;
            cam.aspect = rt.width / (float)rt.height;
            var r = rot * Quaternion.Euler(0f, 0f, -s.Roll);
            if (s.Dof && lens != Vector2.zero)
            {
                // aperture radius in m: focal length / f-number / 2
                float ap = s.FocalMm / Mathf.Max(0.7f, s.FStop) * 0.5f * 0.001f, focus = Mathf.Max(0.2f, s.Focus);
                Vector2 o = lens * ap;
                cam.transform.SetPositionAndRotation(pos + r * new Vector3(o.x, o.y, 0f), r);
                float n = cam.nearClipPlane, top = n * Mathf.Tan(s.Fov * 0.5f * Mathf.Deg2Rad), right = top * cam.aspect;
                float sx = -o.x * n / focus, sy = -o.y * n / focus;   // shear so the focus plane lines up across samples
                cam.projectionMatrix = Matrix4x4.Frustum(-right + sx, right + sx, -top + sy, top + sy, n, cam.farClipPlane);
            }
            else
            {
                cam.transform.SetPositionAndRotation(pos, r);
                cam.ResetProjectionMatrix();
            }
            if (_flash != null) _flash.enabled = s.Flash;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            cam.ResetProjectionMatrix();
            if (_flash != null) _flash.enabled = false;
        }

        /// <summary>Blends sample number n (0-based) into the running average in accum.</summary>
        public static void Accumulate(RenderTexture sample, RenderTexture accum, int n)
        {
            if (n == 0 || Mat == null) { Graphics.Blit(sample, accum); return; }
            Mat.SetFloat("_Weight", 1f / (n + 1));
            Graphics.Blit(sample, accum, Mat, 1);
        }

        /// <summary>Colour filter, vignette and frame from src into dst.</summary>
        public static void Finish(Texture src, RenderTexture dst, PhotoSettings s)
        {
            if (Mat == null) { Graphics.Blit(src, dst); return; }
            Mat.SetFloat("_Mode", s.Filter);
            Mat.SetFloat("_Exposure", s.Exposure);
            Mat.SetFloat("_Vignette", s.Vignette);
            Mat.SetFloat("_Frame", s.Frame);
            Mat.SetFloat("_Aspect", dst.width / (float)dst.height);
            Graphics.Blit(src, dst, Mat, 0);
        }

        public static RenderTexture NewRT(int w, int h, bool hdr, int aa)
        {
            var rt = new RenderTexture(w, h, 24, hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32) { antiAliasing = aa };
            rt.Create();
            return rt;
        }

        /// <summary>Full photo, synchronous: all lens samples, filter, frame. Caller destroys the texture.</summary>
        public static Texture2D Capture(Vector3 pos, Quaternion rot, PhotoSettings s, int w, int h)
        {
            int n = s.Dof ? DofSamplesCapture : 1;
            var sample = NewRT(w, h, false, 4);
            var accum = NewRT(w, h, true, 1);
            var final = NewRT(w, h, false, 1);
            for (int i = 0; i < n; i++)
            {
                RenderSample(pos, rot, s, sample, LensSample(i, n));
                Accumulate(sample, accum, i);
            }
            Finish(accum, final, s);
            var prev = RenderTexture.active;
            RenderTexture.active = final;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            foreach (var rt in new[] { sample, accum, final }) { rt.Release(); Object.Destroy(rt); }
            return tex;
        }
    }
}
