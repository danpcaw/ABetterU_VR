using UnityEditor;
using UnityEngine;

/// Switches on the Meta settings the headset camera needs, once, when the project loads: passthrough support (not required, so the
/// app still runs as a normal VR app) and passthrough camera access. The Meta manifest step then adds the HEADSET_CAMERA permission
/// at build time. Editor-only; does nothing if both are already on.
[InitializeOnLoad]
public static class EnableCameraAccess
{
    static EnableCameraAccess() => EditorApplication.delayCall += Apply;

    [MenuItem("A Better U/Enable headset camera access")]
    static void Apply()
    {
        var config = OVRProjectConfig.CachedProjectConfig;
        if (config == null) return;
        bool changed = false;
        if (config.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
        { config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported; changed = true; }
        if (!config.isPassthroughCameraAccessEnabled)
        { config.isPassthroughCameraAccessEnabled = true; changed = true; }
        if (!changed) return;
        OVRProjectConfig.CommitProjectConfig(config);
        Debug.Log("[Camera] Enabled passthrough support and camera access in the Meta project config (adds horizonos.permission.HEADSET_CAMERA at build).");
    }
}
