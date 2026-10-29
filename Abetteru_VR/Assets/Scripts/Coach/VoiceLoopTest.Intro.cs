using System.Collections;
using UnityEngine;

namespace ABetterU.Coach
{
    public partial class VoiceLoopTest
    {
        // Atlas introduces herself once per launch, right after sign-in, and says plainly what she is and is not.
        static bool introduced;

        string IntroText => $"Hi, I'm {WakeName}, your A Better U voice assistant. " +
            "I'm here to help you get things done, like logging your food, saving your workouts and finding your way around. " +
            "I don't give medical advice or fitness advice, so please speak to a professional for that. " +
            $"Just say, Hey {WakeName}, whenever you need me.";

        /// Called by the app when the first Home screen appears after sign-in. Does nothing if she has already introduced herself.
        public void Introduce()
        {
            if (introduced || busy || tts == null || !tts.Ready) return;
            introduced = true;
            StartCoroutine(IntroRoutine());
        }

        IEnumerator IntroRoutine()
        {
            busy = true;
            PauseHandsFree();                       // she must not hear herself
            bool spoken = false;
            tts.Speak(IntroText, () => spoken = true, (s, secs) => ShowCaption(AssistantState.Speaking, WakeName, s, secs));
            while (!spoken) yield return null;
            Report(AssistantState.Ready, ReadyHeadline, ReadyDetail);
            listenAfter = Time.unscaledTime + 0.7f;
            busy = false;
        }
    }
}
