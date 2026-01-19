using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Services;

public sealed class CleanupService
{
    private const int MinConcurrency = 2;
    private const int MaxConcurrency = 6;

    private readonly Dictionary<string, ICleanupAction> _actions;

    public CleanupService(IEnumerable<ICleanupAction> actions)
    {
        _actions = actions.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<CleanupOutcome>> ExecuteAsync(
        IReadOnlyList<Finding> findings,
        IReadOnlyCollection<string> selectedFindingIds,
        SafetyPolicy policy,
        CancellationToken ct,
        IProgress<CleanupProgress>? progress = null)
    {
        var selectedSet = selectedFindingIds as HashSet<string> ?? new HashSet<string>(selectedFindingIds);
        var selected = findings.Where(finding => selectedSet.Contains(finding.Id)).ToList();
        var totalCount = selected.Count;
        if (totalCount == 0)
        {
            return Array.Empty<CleanupOutcome>();
        }

        var outcomes = new CleanupOutcome[totalCount];
        var processedCount = 0;
        var deletedCount = 0;
        var skippedCount = 0;
        var maxConcurrency = Math.Clamp(Environment.ProcessorCount, MinConcurrency, MaxConcurrency);
        progress?.Report(new CleanupProgress(0, totalCount, 0, 0));

        var batchOutcomes = await TryExecuteBatchAsync(selected, policy, ct).ConfigureAwait(false);
        if (batchOutcomes is not null)
        {
            for (var index = 0; index < batchOutcomes.Count; index++)
            {
                var outcome = batchOutcomes[index];
                outcomes[index] = outcome;
                processedCount++;
                if (outcome.Success)
                {
                    deletedCount++;
                }
                else
                {
                    skippedCount++;
                }

                progress?.Report(new CleanupProgress(processedCount, totalCount, deletedCount, skippedCount));
            }

            return outcomes;
        }

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(totalCount);

        for (var index = 0; index < totalCount; index++)
        {
            var currentIndex = index;
            var finding = selected[currentIndex];
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var outcome = await ExecuteFindingAsync(finding, policy, ct).ConfigureAwait(false);
                    outcomes[currentIndex] = outcome;

                    var processed = Interlocked.Increment(ref processedCount);
                    if (outcome.Success)
                    {
                        Interlocked.Increment(ref deletedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref skippedCount);
                    }

                    progress?.Report(new CleanupProgress(
                        processed,
                        totalCount,
                        Volatile.Read(ref deletedCount),
                        Volatile.Read(ref skippedCount)));
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return outcomes;
    }

    private async Task<CleanupOutcome> ExecuteFindingAsync(Finding finding, SafetyPolicy policy, CancellationToken ct)
    {
        if (!_actions.TryGetValue(finding.RecommendedAction, out var action))
        {
            return new CleanupOutcome(
                finding.Id,
                false,
                "No cleanup action registered.",
                0,
                CleanupOutcomeCategories.SkippedOther);
        }

        return await ExecuteFindingAsync(finding, action, policy, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CleanupOutcome>?> TryExecuteBatchAsync(
        IReadOnlyList<Finding> findings,
        SafetyPolicy policy,
        CancellationToken ct)
    {
        if (findings.Count == 0)
        {
            return Array.Empty<CleanupOutcome>();
        }

        var actionId = findings[0].RecommendedAction;
        if (string.Equals(actionId, CleanupActions.EmptyRecycleBin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (findings.Any(finding => !string.Equals(finding.RecommendedAction, actionId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!_actions.TryGetValue(actionId, out var action) || !action.SupportsBatch)
        {
            return null;
        }

        var validated = new List<Finding>();
        var outcomes = new CleanupOutcome[findings.Count];

        for (var index = 0; index < findings.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var finding = findings[index];
            var safety = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);
            if (!safety.Ok)
            {
                outcomes[index] = new CleanupOutcome(
                    finding.Id,
                    false,
                    $"Failed safety recheck: {safety.Reason}",
                    0,
                    safety.ReasonCategory);
                continue;
            }

            validated.Add(finding);
            outcomes[index] = new CleanupOutcome(
                finding.Id,
                false,
                "Pending batch cleanup.",
                0,
                CleanupOutcomeCategories.SkippedOther);
        }

        if (validated.Count == 0)
        {
            return outcomes;
        }

        var batchOutcomes = await action.ExecuteBatchAsync(validated, policy, ct).ConfigureAwait(false);
        if (batchOutcomes.Count != validated.Count)
        {
            return null;
        }

        var batchIndex = 0;
        for (var index = 0; index < findings.Count; index++)
        {
            if (outcomes[index].Message != "Pending batch cleanup.")
            {
                continue;
            }

            outcomes[index] = batchOutcomes[batchIndex];
            batchIndex++;
        }

        return outcomes;
    }

    private static async Task<CleanupOutcome> ExecuteFindingAsync(
        Finding finding,
        ICleanupAction action,
        SafetyPolicy policy,
        CancellationToken ct)
    {
        try
        {
            var safety = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);
            if (!safety.Ok)
            {
                return new CleanupOutcome(
                    finding.Id,
                    false,
                    $"Failed safety recheck: {safety.Reason}",
                    0,
                    safety.ReasonCategory);
            }

            return await action.ExecuteAsync(finding, policy, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CleanupOutcome(
                finding.Id,
                false,
                ex.Message,
                0,
                CleanupOutcomeCategories.SkippedOther);
        }
    }
}
