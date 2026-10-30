using UnityEngine;

namespace ABetterU.Coach
{
    /// Four rounded corner brackets and a caption, fixed in front of the user's gaze like a camera viewfinder. While scanning, the
    /// real room shows through passthrough behind it, so looking down at a plate you can see what is inside the frame.
    /// The size is an estimate of what the camera captures (about 56 degrees across); the whole camera frame is what gets analysed.
    public class ViewFinder : MonoBehaviour
    {
        const float Distance = 0.7f, HalfW = 0.37f, HalfH = 0.27f, Arm = 0.11f, Thick = 0.009f;
        static readonly Color Colour = new Color(0.45f, 0.95f, 0.88f);
        Transform head;
        TextMesh caption;

        // The headset's camera sits higher than your eyes, so what it captures is centred a little above where you look. The frame is
        // tilted up by this many degrees to match, and the right thumbstick nudges it (up or down) until the brackets line up. Saved.
        public static float UpDegrees
        {
            get => PlayerPrefs.GetFloat("viewfinder_up_deg_v2", -14f);
            set => PlayerPrefs.SetFloat("viewfinder_up_deg_v2", Mathf.Clamp(value, -30f, 25f));
        }

        public static ViewFinder Create(Transform head)
        {
            var go = new GameObject("Viewfinder");
            var finder = go.AddComponent<ViewFinder>();
            finder.head = head;
            finder.Build();
            finder.LateUpdate();
            return finder;
        }

        void Build()
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    var corner = new Vector3(sx * HalfW, sy * HalfH, 0f);
                    Bar(corner, corner - new Vector3(sx * Arm, 0f, 0f));       // one arm along the top or bottom edge
                    Bar(corner, corner - new Vector3(0f, sy * Arm, 0f));       // one arm down the side
                }
            caption = VRUi.Label(transform, "", new Vector3(0f, -HalfH - 0.05f, 0f), 0.0036f, Colour);
        }

        // A bar with rounded ends: a thin box between two points, with a small sphere on each end (which also rounds the corner joint).
        void Bar(Vector3 a, Vector3 b)
        {
            var box = VRUi.Box(transform, "Bar", (a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x) + 0.0001f, Mathf.Abs(b.y - a.y) + 0.0001f, Thick), Colour, false);
            foreach (var end in new[] { a, b })
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "Cap";
                dot.transform.SetParent(transform, false);
                dot.transform.localPosition = end;
                dot.transform.localScale = Vector3.one * Thick * 1.1f;
                Destroy(dot.GetComponent<Collider>());
                var m = GymRoom.MakeMaterial(GymRoom.AcquireTemplate(), Colour, 0.2f, 0f);
                if (m != null) dot.GetComponent<Renderer>().sharedMaterial = m;
            }
        }

        public void LateUpdate()
        {
            if (head == null) return;
            // Right thumbstick up or down moves the frame up or down while aiming.
            float stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;
            if (Mathf.Abs(stick) > 0.3f) UpDegrees += stick * 8f * Time.unscaledDeltaTime;

            if (caption != null) caption.text = $"Left trigger to capture  -  right stick moves the frame ({UpDegrees:+0;-0;0} deg)";
            var aim = head.rotation * Quaternion.Euler(-UpDegrees, 0f, 0f);       // pitched up from straight ahead
            transform.SetPositionAndRotation(head.position + aim * Vector3.forward * Distance, aim);
        }
    }
}
