using System;
using System.Collections;
using System.IO;
using ABetterU.Coach;
using UnityEditor;
using UnityEngine;

/// Captures the actual rendered Game view without relying on Quest's screenshot shortcut.
/// Editor-only: excluded from headset builds. No account requests or sample data are introduced.
public static class WellnessUiCapture
{
    const string Menu = "A Better U/Capture UI screenshot (Play Mode)";
    static bool capturing;

    [MenuItem(Menu)]
    static void Capture()
    {
        var flow = UnityEngine.Object.FindAnyObjectByType<AppFlow>();
        if (!Application.isPlaying || flow == null)
        {
            EditorUtility.DisplayDialog("Capture UI", "Enter Play Mode and open the screen you want to capture first.", "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanel("Save UI screenshot", "",
            "A-Better-U-" + flow.Current + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), "png");
        if (string.IsNullOrEmpty(path)) return;

        // A Game view must render for WaitForEndOfFrame to run in the Editor.
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        capturing = true;
        flow.StartCoroutine(SaveFrame(path));
    }

    [MenuItem(Menu, true)]
    static bool CanCapture() => EditorApplication.isPlaying && !capturing;

    static IEnumerator SaveFrame(string path)
    {
        // Give the Game view time to repaint after the save dialog closes.
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D frame = null;
        try
        {
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null) throw new InvalidOperationException("The Game view did not produce a frame.");
            File.WriteAllBytes(path, frame.EncodeToPNG());
            Debug.Log("[UI Capture] Saved " + frame.width + "x" + frame.height + " screenshot: " + path);
            EditorUtility.RevealInFinder(path);
        }
        catch (Exception e)
        {
            Debug.LogError("[UI Capture] " + e.Message);
        }
        finally
        {
            if (frame != null) UnityEngine.Object.Destroy(frame);
            capturing = false;
        }
    }

    // Stopping Play Mode cancels its coroutines; allow another capture on the next run.
    [InitializeOnLoadMethod]
    static void ResetOnModeChange()
    {
        EditorApplication.playModeStateChanged += _ => capturing = false;
    }
}
