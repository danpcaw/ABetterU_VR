using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ABetterU.Coach
{
    /// A real mirror: a second camera sits where the user's eye would be if it were reflected through the mirror
    /// plane, looks back through the mirror's rectangle (an off-axis frustum), and draws into a texture shown on the
    /// glass quad (which flips it left-right). It only renders while the user is facing the mirror, to spare the headset.
    public class MirrorReflection : MonoBehaviour
    {
        Transform eye;
        Camera cam;
        RenderTexture rt;

        public void Init(Transform eye, Renderer display)
        {
            this.eye = eye;

            var go = new GameObject("MirrorCamera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;                                   // switched on per frame, only when the mirror is in view
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cam.cullingMask = ~(1 << GymRoom.MirrorDisplayLayer);  // everything except the glass itself
            cam.stereoTargetEye = StereoTargetEyeMask.None;        // an ordinary camera, not one of the headset's eyes
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.farClipPlane = 30f;
            cam.depth = -10f;

            // Wide and short, matching the mirror's shape (5.0 m x 2.1 m), kept small to protect frame rate.
            rt = new RenderTexture(1024, 432, 16) { name = "MirrorRT", antiAliasing = 1 };
            cam.targetTexture = rt;

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;

            // The left-right flip of a mirror image is done by the glass quad's negative X scale (see GymRoom).
            if (display != null) display.sharedMaterial.mainTexture = rt;
        }

        void LateUpdate()
        {
            if (cam == null || eye == null) return;

            float planeZ = GymRoom.MirrorPlaneZ;
            Vector3 e = eye.position;

            // Only draw when you are in front of the glass and roughly facing it.
            bool visible = e.z < planeZ - 0.2f && eye.forward.z > -0.2f;
            cam.enabled = visible;
            if (!visible) return;

            // The user's eye reflected through the mirror plane (z = planeZ), looking back into the room (-Z).
            Vector3 virtualEye = new Vector3(e.x, e.y, 2f * planeZ - e.z);
            cam.transform.SetPositionAndRotation(virtualEye, Quaternion.LookRotation(Vector3.back, Vector3.up));

            // Off-axis frustum through the mirror rectangle. This camera looks along -Z, so its right is world -X.
            float d = virtualEye.z - planeZ;                       // distance from the camera to the glass plane
            float near = d + 0.01f;                                // clips the wall and frame that sit behind the glass
            float k = near / d;
            float left = (virtualEye.x - GymRoom.MirrorXMax) * k;
            float right = (virtualEye.x + GymRoom.MirrorXMax) * k;
            float bottom = (GymRoom.MirrorYMin - virtualEye.y) * k;
            float top = (GymRoom.MirrorYMax - virtualEye.y) * k;

            cam.nearClipPlane = near;
            cam.projectionMatrix = Matrix4x4.Frustum(left, right, bottom, top, near, cam.farClipPlane);
        }

        void OnDestroy()
        {
            if (rt != null) rt.Release();
        }
    }
}
