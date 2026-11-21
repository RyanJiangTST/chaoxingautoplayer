namespace ChaoxingAutoPlayer.Models;

public enum CursorBehavior
{
    PauseOnLeave,
    ContinueWithoutCursor,
}

public enum InteractiveQuizMode
{
    PauseAndPrompt,
    AutoAttempt,
}

public sealed class PlayerSettings
{
    public int Version { get; set; } = 1;

    public bool Muted { get; set; } = true;

    public CursorBehavior CursorBehavior { get; set; } = CursorBehavior.ContinueWithoutCursor;

    public bool LockReady { get; set; }

    public InteractiveQuizMode QuizMode { get; set; } = InteractiveQuizMode.AutoAttempt;

    public int PreferredOption { get; set; } = 1;

    public int SecondaryOption { get; set; } = 2;

    public bool TryRemainingOptions { get; set; } = true;

    public int MaxQuizAttempts { get; set; } = 4;

    public PlayerSettings Clone() => new()
    {
        Version = Version,
        Muted = Muted,
        CursorBehavior = CursorBehavior,
        LockReady = LockReady,
        QuizMode = QuizMode,
        PreferredOption = PreferredOption,
        SecondaryOption = SecondaryOption,
        TryRemainingOptions = TryRemainingOptions,
        MaxQuizAttempts = MaxQuizAttempts,
    };

    public void Normalize()
    {
        Version = 1;
        if (!Enum.IsDefined(CursorBehavior))
        {
            CursorBehavior = CursorBehavior.ContinueWithoutCursor;
        }

        if (!Enum.IsDefined(QuizMode))
        {
            QuizMode = InteractiveQuizMode.AutoAttempt;
        }

        PreferredOption = Math.Clamp(PreferredOption, 1, 4);
        SecondaryOption = Math.Clamp(SecondaryOption, 1, 4);
        if (SecondaryOption == PreferredOption)
        {
            SecondaryOption = PreferredOption % 4 + 1;
        }

        MaxQuizAttempts = Math.Clamp(MaxQuizAttempts, 1, 4);
    }
}
