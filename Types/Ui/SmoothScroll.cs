using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Smooth wheel scrolling for a ScrollRect. Sits on the viewport, which is
// the raycast target, so the wheel reaches it first and never the scroll
// rect's own stepwise handler; each notch moves a target by a fixed number
// of pixels and the view eases toward it every frame. A drag or its
// inertia (the scroll rect's own velocity) takes over at once.
// The wheel's delta is not one per notch everywhere (in VaM one notch reports
// many times that, and a step times the raw delta ran a short page to its end
// in one notch), so a notch is measured: the smallest step seen so far is one
// notch, each event moves that many notches (at least one, at most
// MaxNotches, so a fast spin still runs faster), each StepPixels.
sealed class SmoothScroll : MonoBehaviour, IScrollHandler
{
    public ScrollRect Scroll;
    public float StepPixels = 48f;    // one wheel notch
    public float Seconds = 0.12f;     // ease time constant
    public int MaxNotches = 3;

    float target;
    bool active;
    float notch;                      // the smallest |delta| seen: one notch

    public void OnScroll(PointerEventData e)
    {
        if (Scroll == null || Scroll.content == null || Scroll.viewport == null) return;
        float range = Scroll.content.rect.height - Scroll.viewport.rect.height;
        if (range <= 1f) return;
        float d = e.scrollDelta.y;
        float mag = Mathf.Abs(d);
        if (mag < 1e-4f) return;
        if (notch <= 0f || mag < notch) notch = mag;
        float notches = Mathf.Clamp(Mathf.Round(mag / notch), 1f, MaxNotches);
        if (!active) { target = Scroll.verticalNormalizedPosition; active = true; }
        target = Mathf.Clamp01(target + Mathf.Sign(d) * notches * StepPixels / range);
        Scroll.velocity = Vector2.zero;
    }

    // Ends an ease: the content it aimed at was swapped (a modal's next
    // message) or the view was hidden.
    public void Stop() { active = false; }

    void OnDisable() { active = false; }

    void Update()
    {
        if (!active || Scroll == null) return;
        if (Input.GetMouseButton(0) || Scroll.velocity.sqrMagnitude > 0.01f) { active = false; return; }
        float now = Scroll.verticalNormalizedPosition;
        float next = Mathf.Lerp(now, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, Seconds)));
        if (Mathf.Abs(next - target) < 0.0005f) { next = target; active = false; }
        Scroll.verticalNormalizedPosition = next;
    }
}
