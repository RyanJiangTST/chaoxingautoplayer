using System.Windows;
using ChaoxingAutoPlayer.Models;

namespace ChaoxingAutoPlayer;

public partial class SettingsWindow : Window
{
    public SettingsWindow(PlayerSettings settings)
    {
        InitializeComponent();

        Settings = settings.Clone();
        Settings.Normalize();
        InitializeChoices();
        LoadSettings();
        UpdateQuizOptions();
    }

    public PlayerSettings Settings { get; }

    private void InitializeChoices()
    {
        var choices = new[] { 1, 2, 3, 4 };
        PreferredOptionCombo.ItemsSource = choices;
        SecondaryOptionCombo.ItemsSource = choices;
        MaxAttemptsCombo.ItemsSource = choices;
    }

    private void LoadSettings()
    {
        MuteCheck.IsChecked = Settings.Muted;
        LockReadyCheck.IsChecked = Settings.LockReady;
        PauseCursorRadio.IsChecked = Settings.CursorBehavior == CursorBehavior.PauseOnLeave;
        ContinueCursorRadio.IsChecked = Settings.CursorBehavior == CursorBehavior.ContinueWithoutCursor;
        PauseQuizRadio.IsChecked = Settings.QuizMode == InteractiveQuizMode.PauseAndPrompt;
        AutoQuizRadio.IsChecked = Settings.QuizMode == InteractiveQuizMode.AutoAttempt;
        SkipQuizRadio.IsChecked = Settings.QuizMode == InteractiveQuizMode.Skip;
        PreferredOptionCombo.SelectedItem = Settings.PreferredOption;
        SecondaryOptionCombo.SelectedItem = Settings.SecondaryOption;
        TryRemainingCheck.IsChecked = Settings.TryRemainingOptions;
        MaxAttemptsCombo.SelectedItem = Settings.MaxQuizAttempts;
    }

    private void QuizMode_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoQuizOptions is not null)
        {
            UpdateQuizOptions();
        }
    }

    private void UpdateQuizOptions()
    {
        var enabled = AutoQuizRadio.IsChecked == true;
        AutoQuizOptions.IsEnabled = enabled;
        TryRemainingCheck.IsEnabled = enabled;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Settings.Muted = MuteCheck.IsChecked == true;
        Settings.LockReady = LockReadyCheck.IsChecked == true;
        Settings.CursorBehavior = ContinueCursorRadio.IsChecked == true
            ? CursorBehavior.ContinueWithoutCursor
            : CursorBehavior.PauseOnLeave;
        Settings.QuizMode = SkipQuizRadio.IsChecked == true
            ? InteractiveQuizMode.Skip
            : AutoQuizRadio.IsChecked == true
                ? InteractiveQuizMode.AutoAttempt
                : InteractiveQuizMode.PauseAndPrompt;
        Settings.PreferredOption = PreferredOptionCombo.SelectedItem is int preferred ? preferred : 1;
        Settings.SecondaryOption = SecondaryOptionCombo.SelectedItem is int secondary ? secondary : 2;
        Settings.TryRemainingOptions = TryRemainingCheck.IsChecked == true;
        Settings.MaxQuizAttempts = MaxAttemptsCombo.SelectedItem is int attempts ? attempts : 4;
        Settings.Normalize();

        DialogResult = true;
    }
}
