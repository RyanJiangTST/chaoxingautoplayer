using ChaoxingAutoPlayer.Models;

namespace ChaoxingAutoPlayer.Automation;

public sealed record InteractiveQuizOption(int Index, string Text, bool MarkedCorrect);

public sealed record InteractiveQuizPrompt(
    string Question,
    string QuestionType,
    IReadOnlyList<InteractiveQuizOption> Options);

public sealed record InteractiveQuizAttempt(IReadOnlyList<int> OptionIndexes, string Source);

/// <summary>
/// Produces ordered answer attempts. Version 0.1.5 uses page metadata and configured option order;
/// the future AI build can replace this implementation without changing the browser transport.
/// </summary>
public interface IInteractiveQuizSolver
{
    IReadOnlyList<InteractiveQuizAttempt> BuildAttempts(
        InteractiveQuizPrompt prompt,
        PlayerSettings settings);
}

public sealed class PageMetadataQuizSolver : IInteractiveQuizSolver
{
    public IReadOnlyList<InteractiveQuizAttempt> BuildAttempts(
        InteractiveQuizPrompt prompt,
        PlayerSettings settings)
    {
        if (settings.QuizMode == InteractiveQuizMode.Skip)
        {
            return Array.Empty<InteractiveQuizAttempt>();
        }

        var marked = prompt.Options
            .Where(option => option.MarkedCorrect)
            .Select(option => option.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
        if (marked.Count > 0)
        {
            return new[] { new InteractiveQuizAttempt(marked, "page-answer") };
        }

        if (settings.QuizMode != InteractiveQuizMode.AutoAttempt)
        {
            return Array.Empty<InteractiveQuizAttempt>();
        }

        if (prompt.QuestionType.Contains("多选", StringComparison.OrdinalIgnoreCase)
            || prompt.QuestionType.Contains("multi", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<InteractiveQuizAttempt>();
        }

        var optionCount = prompt.Options.Count;
        if (optionCount == 0)
        {
            return Array.Empty<InteractiveQuizAttempt>();
        }

        var order = new List<int>();
        AddIfValid(order, settings.PreferredOption - 1, optionCount);
        AddIfValid(order, settings.SecondaryOption - 1, optionCount);

        if (settings.TryRemainingOptions)
        {
            for (var index = 0; index < optionCount; index++)
            {
                AddIfValid(order, index, optionCount);
            }
        }

        return order
            .Take(Math.Min(settings.MaxQuizAttempts, optionCount))
            .Select((index, position) => new InteractiveQuizAttempt(
                new[] { index },
                position == 0 ? "preferred" : position == 1 ? "secondary" : "remaining"))
            .ToList();
    }

    private static void AddIfValid(ICollection<int> order, int index, int optionCount)
    {
        if (index >= 0 && index < optionCount && !order.Contains(index))
        {
            order.Add(index);
        }
    }
}
