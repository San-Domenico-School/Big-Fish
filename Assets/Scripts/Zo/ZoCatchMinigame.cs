using System;
using UnityEngine;

namespace BigFish
{
    /// <summary>
    /// Base class for the "land the fish" minigame that runs after a bite.
    ///
    /// To make a different catch UI: create a new script that inherits from this,
    /// put it on any GameObject, and drag it into ZoFishingModeController's
    /// "Catch Minigame" slot. The controller handles input and fishing state;
    /// your minigame only decides visuals + whether the fish was caught.
    ///
    ///   OnBegin(fish)            -> show your UI, reset your state
    ///   Tick(dt, pressed, held)  -> called every frame while running
    ///                               (pressed = Space / left click this frame, held = still down)
    ///   Finish(true / false)     -> call this when the fish is caught / escapes
    ///   OnEnd()                  -> hide your UI (called on finish AND on cancel)
    /// </summary>
    public abstract class ZoCatchMinigame : MonoBehaviour
    {
        /// <summary>Fired once with true (caught) or false (got away).</summary>
        public event Action<bool> Finished;

        public bool IsRunning { get; private set; }
        public CatchInfo CurrentFish { get; private set; }

        /// <summary>
        /// Optional text the controller shows when the minigame ends (e.g. "Line parted").
        /// Leave null to use the controller's default "You caught X!" / "It got away..." text.
        /// </summary>
        public virtual string ResultMessage => null;

        /// <summary>True if this minigame shows its own catch popup, so the controller stays quiet on success.</summary>
        public virtual bool ShowsOwnCatchCard => false;

        public void Begin(CatchInfo fish)
        {
            if (IsRunning)
                Cancel();

            CurrentFish = fish;
            IsRunning = true;
            OnBegin(fish);
        }

        /// <summary>Stops without reporting a result (e.g. player left fishing mode).</summary>
        public void Cancel()
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            OnEnd();
        }

        public abstract void Tick(float deltaTime, bool actionPressed, bool actionHeld);

        protected abstract void OnBegin(CatchInfo fish);
        protected abstract void OnEnd();

        protected void Finish(bool caught)
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            OnEnd();
            Finished?.Invoke(caught);
        }
    }
}
