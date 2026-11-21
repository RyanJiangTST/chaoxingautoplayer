namespace ChaoxingAutoPlayer.Automation;

public static class AutomationPolicy
{
    public static bool ShouldRun(
        bool sleeping,
        bool lockActive,
        bool lockArmed,
        bool manualRequested,
        bool pointerInside,
        bool windowActive,
        bool continueWithoutCursor = false) =>
        !sleeping
        && ((lockActive && lockArmed)
            || (manualRequested && (continueWithoutCursor || (pointerInside && windowActive))));
}
