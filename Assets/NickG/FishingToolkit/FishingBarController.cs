using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BigFish.NickG.FishingToolkit
{
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class FishingBarController : MonoBehaviour
    {
        public FishingBarSettings settings;
        public bool ManualTestMode { get; set; }
        public bool KeyboardInputEnabled { get; set; } = true;
        public bool IsVisible { get; private set; } = true;
        public FishingTimingRound Round { get; private set; } = new FishingTimingRound();
        public VisualElement Root { get; private set; }
        public event Action<FishingTimingState> RoundFinished;
        private PanelRenderer panel;
        private VisualElement marker, left, target, right, timerFill;
        private Label status, timerLabel, instruction, holdLabel;
        private Button action, restart;
        private FishingTimingState lastReported;
        private bool focus = true;
        private VisualElement overlay, card, dragHandle;
        private bool positioned, keyboardArmed = true;
        private int dragPointer = -1;
        private Vector2 dragOffset;

        private void OnEnable()
        {
            panel = GetComponent<PanelRenderer>();
            panel.RegisterUIReloadCallback(Bind);
        }
        private void OnDisable()
        {
            if (panel != null) panel.UnregisterUIReloadCallback(Bind);
            Unbind();
        }
        private void Unbind()
        {
            EndDrag();
            if (dragHandle != null)
            {
                dragHandle.UnregisterCallback<PointerDownEvent>(BeginDrag);
                dragHandle.UnregisterCallback<PointerMoveEvent>(MoveDrag);
                dragHandle.UnregisterCallback<PointerUpEvent>(FinishDrag);
                dragHandle.UnregisterCallback<PointerCaptureOutEvent>(LoseDrag);
            }
            if (card != null)
            {
                card.UnregisterCallback<PointerDownEvent>(ArmKeyboard);
                card.UnregisterCallback<GeometryChangedEvent>(KeepInBounds);
            }
            if (overlay != null) overlay.UnregisterCallback<GeometryChangedEvent>(KeepInBounds);
            if (action != null) action.clicked -= PrimaryAction;
            if (restart != null) restart.clicked -= RestartRound;
            Root = null; overlay = card = dragHandle = null;
            positioned = false;
        }
        private void Bind(PanelRenderer renderer, VisualElement root, int version)
        {
            Unbind(); Root = root;
            marker = root.Q("marker"); left = root.Q("left-zone"); target = root.Q("target-zone"); right = root.Q("right-zone");
            timerFill = root.Q("timer-fill"); status = root.Q<Label>("status-label");
            timerLabel = root.Q<Label>("timer-label"); instruction = root.Q<Label>("instruction-label");
            holdLabel = root.Q<Label>("hold-label");
            action = root.Q<Button>("action-button"); restart = root.Q<Button>("restart-button");
            if (marker == null || target == null || action == null || timerLabel == null || holdLabel == null)
            { Debug.LogError("[Fishing Toolkit] Assign FishingBar.uxml as the Panel Renderer Source Asset.", this); return; }
            // Physical Space is handled once by this controller, not again as a focused button submit.
            action.focusable = restart.focusable = false;
            action.clicked += PrimaryAction; restart.clicked += RestartRound;
            root.pickingMode = PickingMode.Ignore;
            // Empty screen space belongs to the game or panels below this window.
            if (root.panel != null) root.panel.visualTree.pickingMode = PickingMode.Ignore;
            overlay = root.Q("fishing-root"); card = root.Q("fishing-card");
            dragHandle = root.Q("drag-handle");
            overlay.pickingMode = PickingMode.Ignore;
            card.pickingMode = PickingMode.Position;
            card.RegisterCallback<PointerDownEvent>(ArmKeyboard);
            card.RegisterCallback<GeometryChangedEvent>(KeepInBounds);
            overlay.RegisterCallback<GeometryChangedEvent>(KeepInBounds);
            if (dragHandle != null)
            {
                dragHandle.RegisterCallback<PointerDownEvent>(BeginDrag);
                dragHandle.RegisterCallback<PointerMoveEvent>(MoveDrag);
                dragHandle.RegisterCallback<PointerUpEvent>(FinishDrag);
                dragHandle.RegisterCallback<PointerCaptureOutEvent>(LoseDrag);
            }
            SetVisible(IsVisible);
            Render();
        }
        private void Update()
        {
            if (ManualTestMode || !focus || Root == null || !IsVisible) return;
            Tick(Time.unscaledDeltaTime);
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                keyboardArmed = IsPointerOverWindow(mouse.position.ReadValue());
            if (!KeyboardInputEnabled || !keyboardArmed) return;
            var focused = Root.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && focused != Root && !Root.Contains(focused)) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.rKey.wasPressedThisFrame) RestartRound();
            else if (keyboard.spaceKey.wasPressedThisFrame) PrimaryAction();
        }
        private void OnApplicationFocus(bool hasFocus) { focus = hasFocus; if (!hasFocus) EndDrag(); }

        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (!visible) EndDrag();
            if (overlay != null) overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            keyboardArmed = visible;
        }

        // For host games that read raw pointer input: skip their world action only over this window.
        // Accepts Input System/Camera screen coordinates (origin at the bottom-left).
        public bool IsPointerOverWindow(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || !IsVisible || card == null || Root?.panel == null ||
                card.resolvedStyle.display == DisplayStyle.None) return false;
            Vector2 point = RuntimePanelUtils.ScreenToPanel(Root.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return card.worldBound.Contains(point);
        }

        private void ArmKeyboard(PointerDownEvent evt) { keyboardArmed = true; }
        private void BeginDrag(PointerDownEvent evt)
        {
            if (evt.button != 0 || dragPointer >= 0) return;
            Vector2 origin = overlay.WorldToLocal(card.worldBound.position);
            dragOffset = (Vector2)overlay.WorldToLocal(evt.position) - origin;
            card.style.translate = new Translate(0, 0);
            card.style.bottom = StyleKeyword.Auto;
            card.style.left = origin.x; card.style.top = origin.y;
            positioned = true; keyboardArmed = true;
            dragPointer = evt.pointerId;
            dragHandle.CapturePointer(dragPointer);
            evt.StopPropagation();
        }
        private void MoveDrag(PointerMoveEvent evt)
        {
            if (evt.pointerId != dragPointer || !dragHandle.HasPointerCapture(dragPointer)) return;
            SetWindowPosition((Vector2)overlay.WorldToLocal(evt.position) - dragOffset);
            evt.StopPropagation();
        }
        private void FinishDrag(PointerUpEvent evt)
        {
            if (evt.pointerId != dragPointer) return;
            SetWindowPosition((Vector2)overlay.WorldToLocal(evt.position) - dragOffset);
            EndDrag(); evt.StopPropagation();
        }
        private void LoseDrag(PointerCaptureOutEvent evt) { dragPointer = -1; }
        private void EndDrag()
        {
            int pointer = dragPointer; dragPointer = -1;
            if (pointer >= 0 && dragHandle != null && dragHandle.HasPointerCapture(pointer))
                dragHandle.ReleasePointer(pointer);
        }
        private void KeepInBounds(GeometryChangedEvent evt)
        {
            if (positioned && card != null) SetWindowPosition(new Vector2(card.layout.x, card.layout.y));
        }
        private void SetWindowPosition(Vector2 position)
        {
            float width = overlay.contentRect.width, height = overlay.contentRect.height;
            if (width <= 0 || height <= 0) return;
            card.style.left = Mathf.Clamp(position.x, 0, Mathf.Max(0, width - card.layout.width));
            card.style.top = Mathf.Clamp(position.y, 0, Mathf.Max(0, height - card.layout.height));
        }
        public void PrimaryAction()
        {
            if (Round.State != FishingTimingState.Playing) RestartRound();
            Round.Tap();
            Render(); ReportResult();
        }
        public void RestartRound()
        {
            Round.Start(settings != null ? settings.roundSeconds : 8f, settings != null ? settings.tapsPerSecondAtRight : 6f,
                settings != null ? settings.targetStart : .3f, settings != null ? settings.targetEnd : .7f,
                settings != null ? settings.holdSeconds : 2f);
            lastReported = FishingTimingState.Playing; Render();
        }
        public void Tick(float seconds) { Round.Advance(seconds); Render(); ReportResult(); }
        private void ReportResult()
        {
            var state = Round.State;
            if (state == lastReported || state == FishingTimingState.Ready || state == FishingTimingState.Playing) return;
            lastReported = state; RoundFinished?.Invoke(state);
        }
        private void Render()
        {
            if (Root == null || marker == null || timerFill == null) return;
            float a = Round.State == FishingTimingState.Ready && settings != null ? Mathf.Clamp(settings.targetStart, 0, .95f) : Round.TargetStart;
            float b = Round.State == FishingTimingState.Ready && settings != null ? Mathf.Clamp(settings.targetEnd, a + .05f, 1) : Round.TargetEnd;
            left.style.width = Length.Percent(a * 100); target.style.width = Length.Percent((b-a) * 100); right.style.width = Length.Percent((1-b) * 100);
            if (settings != null)
            {
                left.style.backgroundColor = right.style.backgroundColor = settings.outsideColor;
                target.style.backgroundColor = settings.targetColor; timerFill.style.backgroundColor = settings.timerColor;
            }
            marker.style.left = Length.Percent(Round.Position * 100);
            bool ready = Round.State == FishingTimingState.Ready;
            timerFill.style.width = Length.Percent(ready ? 100 : Round.Remaining / Round.Duration * 100);
            float seconds = ready ? (settings != null ? settings.roundSeconds : 8) : Round.Remaining;
            timerLabel.text = "Timer   " + seconds.ToString("0.0") + "s";
            float required = ready && settings != null ? settings.holdSeconds : Round.RequiredHoldSeconds;
            holdLabel.text = "In green  " + Round.HoldProgressSeconds.ToString("0.0") + " / " + required.ToString("0.0") + " s";
            status.text = ready ? "READY" : Round.State == FishingTimingState.Playing ?
                (Round.Position < Round.TargetStart ? "TAP FASTER" : Round.Position > Round.TargetEnd ? "SLOW DOWN" : "KEEP THIS PACE") :
                Round.State == FishingTimingState.Caught ? "CAUGHT!" : "TIME UP";
            instruction.text = Round.State == FishingTimingState.Caught ? "Nice! You stayed in green long enough." :
                Round.State == FishingTimingState.TimeUp ? "Time is up. Keep a steady pace and try again." :
                "Tap SPACE at a steady pace. Stay in green for " + required.ToString("0.#") + " seconds.";
            action.text = Round.State == FishingTimingState.Playing ? "TAP  [SPACE]" : ready ? "START  [SPACE]" : "TRY AGAIN  [SPACE]";
            restart.text = "RESTART  [R]";
            restart.style.display = Round.State == FishingTimingState.Playing || ready ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
