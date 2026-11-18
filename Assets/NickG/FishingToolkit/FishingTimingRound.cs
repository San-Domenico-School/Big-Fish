using System;
using System.Collections.Generic;

namespace BigFish.NickG.FishingToolkit
{
    public enum FishingTimingState { Ready, Playing, Caught, TimeUp }

    // The same normalized position drives both the visible marker and catch judgment.
    public sealed class FishingTimingRound
    {
        public FishingTimingState State { get; private set; }
        public float Position { get; private set; } = .08f;
        public float Remaining { get; private set; }
        public float Duration { get; private set; } = 8f;
        public float TargetStart { get; private set; } = .3f;
        public float TargetEnd { get; private set; } = .7f;
        public float TapRate { get; private set; }
        public float HoldProgressSeconds { get; private set; }
        public float RequiredHoldSeconds { get; private set; } = 2f;
        public bool IsInTarget => Position >= TargetStart && Position <= TargetEnd;
        private float tapsAtRight = 6f;
        private double elapsed, held;
        private readonly Queue<double> taps = new Queue<double>();

        public void Start(float seconds, float tapsPerSecondAtRight, float left, float right, float holdSeconds = 2f)
        {
            Duration = Safe(seconds, 8f, 1f, 120f);
            tapsAtRight = Safe(tapsPerSecondAtRight, 6f, 2f, 20f);
            RequiredHoldSeconds = Safe(holdSeconds, 2f, .25f, Duration);
            TargetStart = Safe(left, .3f, 0f, .95f);
            TargetEnd = Safe(right, .7f, TargetStart + .05f, 1f);
            elapsed = 0; Position = .08f; Remaining = Duration;
            held = 0; HoldProgressSeconds = 0; TapRate = 0; taps.Clear();
            State = FishingTimingState.Playing;
        }
        public void Tap()
        {
            if (State != FishingTimingState.Playing) return;
            taps.Enqueue(elapsed);
            ReadPace();
        }
        public void Advance(float seconds)
        {
            if (State != FishingTimingState.Playing || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            double end = Math.Min(Duration, elapsed + seconds);
            // Small steps keep hold time stable even when a rendering frame is late.
            while (elapsed < end && State == FishingTimingState.Playing)
            {
                double dt = Math.Min(1.0 / 120.0, end - elapsed);
                elapsed += dt;
                ReadPace();
                float aim = Math.Min(1f, TapRate / tapsAtRight);
                Position = (float)(aim + (Position - aim) * Math.Exp(-5 * dt));
                held = IsInTarget ? held + dt : 0;
                HoldProgressSeconds = (float)Math.Min(held, RequiredHoldSeconds);
                Remaining = (float)Math.Max(0, Duration - elapsed);
                if (Remaining <= 0) State = FishingTimingState.TimeUp;
                else if (held + .000001 >= RequiredHoldSeconds) State = FishingTimingState.Caught;
            }
        }
        private void ReadPace()
        {
            // Each key-down is one tap. Holding Space does not add repeat taps.
            while (taps.Count > 0 && elapsed - taps.Peek() >= 1.0) taps.Dequeue();
            TapRate = taps.Count;
        }
        private static float Safe(float v, float fallback, float min, float max)
        { return float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Max(min, Math.Min(max, v)); }
    }
}
