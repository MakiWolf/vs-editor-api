﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.PatternMatching;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.Language.Intellisense.Implementation
{
    [Export(typeof(IAsyncCompletionServiceProvider))]
    [Name(KnownCompletionNames.DefaultCompletionService)]
    [ContentType("text")]
    internal class DefaultCompletionServiceProvider : IAsyncCompletionServiceProvider
    {
        [Import]
        public IPatternMatcherFactory PatternMatcherFactory { get; set; }

        DefaultCompletionService _instance;

        IAsyncCompletionService IAsyncCompletionServiceProvider.GetOrCreate(ITextView textView)
        {
            if (_instance == null)
                _instance = new DefaultCompletionService(PatternMatcherFactory);
            return _instance;
        }
    }

    internal class DefaultCompletionService : IAsyncCompletionService
    {
        readonly IPatternMatcherFactory _patternMatcherFactory;

        internal DefaultCompletionService(IPatternMatcherFactory patternMatcherFactory)
        {
            _patternMatcherFactory = patternMatcherFactory;
        }

        Task<FilteredCompletionModel> IAsyncCompletionService.UpdateCompletionListAsync(
            ImmutableArray<CompletionItem> sortedList, CompletionTriggerReason triggerReason, CompletionFilterReason filterReason,
            ITextSnapshot snapshot, ITrackingSpan applicableSpan, ImmutableArray<CompletionFilterWithState> filters, ITextView view, CancellationToken token)
        {
            // Filter by text
            var filterText = applicableSpan.GetText(snapshot);
            if (string.IsNullOrWhiteSpace(filterText))
            {
                // There is no text filtering. Just apply user filters, sort alphabetically and return.
                IEnumerable<CompletionItem> listFiltered = sortedList;
                if (filters.Any(n => n.IsSelected))
                {
                    listFiltered = sortedList.Where(n => ShouldBeInCompletionList(n, filters));
                }
                var listSorted = listFiltered.OrderBy(n => n.SortText);
                var listHighlighted = listSorted.Select(n => new CompletionItemWithHighlight(n)).ToImmutableArray();
                return Task.FromResult(new FilteredCompletionModel(listHighlighted, 0, filters));
            }

            // Pattern matcher not only filters, but also provides a way to order the results by their match quality.
            // The relevant CompletionItem is match.Item1, its PatternMatch is match.Item2
            var patternMatcher = _patternMatcherFactory.CreatePatternMatcher(
                filterText,
                new PatternMatcherCreationOptions(System.Globalization.CultureInfo.CurrentCulture, PatternMatcherCreationFlags.IncludeMatchedSpans));

            var matches = sortedList
                // Perform pattern matching
                .Select(completionItem => (completionItem, patternMatcher.TryMatch(completionItem.FilterText)))
                // Pick only items that were matched, unless length of filter text is 1
                .Where(n => (filterText.Length == 1 || n.Item2.HasValue));

            // See which filters might be enabled based on the typed code
            var textFilteredFilters = matches.SelectMany(n => n.Item1.Filters).Distinct();

            // When no items are available for a given filter, it becomes unavailable
            var updatedFilters = ImmutableArray.CreateRange(filters.Select(n => n.WithAvailability(textFilteredFilters.Contains(n.Filter))));

            // Filter by user-selected filters. The value on availableFiltersWithSelectionState conveys whether the filter is selected.
            var filterFilteredList = matches;
            if (filters.Any(n => n.IsSelected))
            {
                filterFilteredList = matches.Where(n => ShouldBeInCompletionList(n.Item1, filters));
            }

            var bestMatch = filterFilteredList.OrderByDescending(n => n.Item2.HasValue).ThenBy(n => n.Item2).FirstOrDefault();
            var listWithHighlights = filterFilteredList.Select(n => n.Item2.HasValue ? new CompletionItemWithHighlight(n.Item1, n.Item2.Value.MatchedSpans) : new CompletionItemWithHighlight(n.Item1)).ToImmutableArray();

            int selectedItemIndex = 0;
            for (int i = 0; i < listWithHighlights.Length; i++)
            {
                if (listWithHighlights[i].CompletionItem == bestMatch.Item1)
                {
                    selectedItemIndex = i;
                    break;
                }
            }

            return Task.FromResult(new FilteredCompletionModel(listWithHighlights, selectedItemIndex, updatedFilters));
        }

        Task<ImmutableArray<CompletionItem>> IAsyncCompletionService.SortCompletionListAsync(
            ImmutableArray<CompletionItem> initialList, CompletionTriggerReason triggerReason, ITextSnapshot snapshot,
            ITrackingSpan applicableToSpan, ITextView view, CancellationToken token)
        {
            return Task.FromResult(initialList.OrderBy(n => n.SortText).ToImmutableArray());
        }

        #region Filtering

        private static bool ShouldBeInCompletionList(
            CompletionItem item,
            ImmutableArray<CompletionFilterWithState> filtersWithState)
        {
            foreach (var filterWithState in filtersWithState.Where(n => n.IsSelected))
            {
                if (item.Filters.Any(n => n == filterWithState.Filter))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }

#if DEBUG && false
    [Export(typeof(IAsyncCompletionItemSourceProvider))]
    [Name("Debug completion item source")]
    [Order(After = "default")]
    [ContentType("any")]
    public class DebugCompletionItemSourceProvider : IAsyncCompletionItemSourceProvider
    {
        DebugCompletionItemSource _instance;

        IAsyncCompletionItemSource IAsyncCompletionItemSourceProvider.GetOrCreate(ITextView textView)
        {
            if (_instance == null)
                _instance = new DebugCompletionItemSource();
            return _instance;
        }
    }

    public class DebugCompletionItemSource : IAsyncCompletionItemSource
    {
        private static readonly AccessibleImageId Icon1 = new AccessibleImageId(new Guid("{ae27a6b0-e345-4288-96df-5eaf394ee369}"), 666, "Icon description");
        private static readonly CompletionFilter Filter1 = new CompletionFilter("Diagnostic", "d", Icon1);
        private static readonly AccessibleImageId Icon2 = new AccessibleImageId(new Guid("{ae27a6b0-e345-4288-96df-5eaf394ee369}"), 2852, "Icon description");
        private static readonly CompletionFilter Filter2 = new CompletionFilter("Snippets", "s", Icon2);
        private static readonly AccessibleImageId Icon3 = new AccessibleImageId(new Guid("{ae27a6b0-e345-4288-96df-5eaf394ee369}"), 473, "Icon description");
        private static readonly CompletionFilter Filter3 = new CompletionFilter("Class", "c", Icon3);
        private static readonly ImmutableArray<CompletionFilter> FilterCollection1 = ImmutableArray.Create(Filter1);
        private static readonly ImmutableArray<CompletionFilter> FilterCollection2 = ImmutableArray.Create(Filter2);
        private static readonly ImmutableArray<CompletionFilter> FilterCollection3 = ImmutableArray.Create(Filter3);
        private static readonly ImmutableArray<char> commitCharacters = ImmutableArray.Create(' ', ';', '.', '<', '(', '[');

        CustomCommitBehavior IAsyncCompletionItemSource.CustomCommit(ITextView view, ITextBuffer buffer, CompletionItem item, ITrackingSpan applicableSpan, char typeChar, CancellationToken token)
        {
            return CustomCommitBehavior.None;
        }

        CommitBehavior IAsyncCompletionItemSource.GetDefaultCommitBehavior(ITextView view, ITextBuffer buffer, CompletionItem item, ITrackingSpan applicableSpan, char typeChar, CancellationToken token)
        {
            return CommitBehavior.None;
        }

        async Task<CompletionContext> IAsyncCompletionItemSource.GetCompletionContextAsync(CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan triggerLocation, CancellationToken token)
        {
            return await Task.FromResult(new CompletionContext(
                ImmutableArray.Create(
                    new CompletionItem("SampleItem<>", this, Icon3, FilterCollection3, string.Empty, false, "SampleItem", "SampleItem<>", "SampleItem", ImmutableArray<AccessibleImageId>.Empty),
                    new CompletionItem("AnotherItem🐱‍👤", this, Icon3, FilterCollection3, string.Empty, false, "AnotherItem", "AnotherItem", "AnotherItem", ImmutableArray.Create(Icon3)),
                    new CompletionItem("Sampling", this, Icon1, FilterCollection1),
                    new CompletionItem("Sampler", this, Icon1, FilterCollection1),
                    new CompletionItem("Sapling", this, Icon2, FilterCollection2, "Sapling is a young tree"),
                    new CompletionItem("OverSampling", this, Icon1, FilterCollection1, "overload"),
                    new CompletionItem("AnotherSample", this, Icon2, FilterCollection2),
                    new CompletionItem("AnotherSampling", this, Icon2, FilterCollection2),
                    new CompletionItem("Simple", this, Icon3, FilterCollection3, "KISS"),
                    new CompletionItem("Simpler", this, Icon3, FilterCollection3, "KISS")
                )));//, true, true, "Suggestion mode description!"));
        }

        async Task<object> IAsyncCompletionItemSource.GetDescriptionAsync(CompletionItem item, CancellationToken token)
        {
            return await Task.FromResult("This is a tooltip for " + item.DisplayText);
        }

        ImmutableArray<char> IAsyncCompletionItemSource.GetPotentialCommitCharacters() => commitCharacters;

        bool IAsyncCompletionItemSource.ShouldCommitCompletion(char typeChar, SnapshotPoint location)
        {
            return true;
        }

        SnapshotSpan? IAsyncCompletionItemSource.ShouldTriggerCompletion(char typeChar, SnapshotPoint triggerLocation)
        {
            var charBeforeCaret = triggerLocation.Subtract(1).GetChar();
            if (commitCharacters.Contains(charBeforeCaret) || triggerLocation.Position == 0)
            {
                // skip the typed character. the applicable span starts at the caret
                return new SnapshotSpan(triggerLocation, 0);
            }
            else
            {
                // include the typed character.
                return new SnapshotSpan(triggerLocation - 1, 1);
            }
        }
    }

#endif
#if DEBUG && true

    [Export(typeof(IAsyncCompletionItemSourceProvider))]
    [Name("Debug HTML completion item source")]
    [Order(After = "default")]
    [ContentType("RazorCSharp")]
    public class DebugHtmlCompletionItemSourceProvider : IAsyncCompletionItemSourceProvider
    {
        DebugHtmlCompletionItemSource _instance;

        IAsyncCompletionItemSource IAsyncCompletionItemSourceProvider.GetOrCreate(ITextView textView)
        {
            if (_instance == null)
                _instance = new DebugHtmlCompletionItemSource();
            return _instance;
        }
    }

    public class DebugHtmlCompletionItemSource : IAsyncCompletionItemSource
    {
        private static readonly ImmutableArray<char> commitCharacters = ImmutableArray.Create(' ', '>', '=');

        CommitBehavior IAsyncCompletionItemSource.CustomCommit(Text.Editor.ITextView view, ITextBuffer buffer, CompletionItem item, ITrackingSpan applicableSpan, char typeChar, CancellationToken token)
        {
            return CommitBehavior.None;
        }

        CommitBehavior IAsyncCompletionItemSource.GetDefaultCommitBehavior(ITextView view, ITextBuffer buffer, CompletionItem item, ITrackingSpan applicableSpan, char typeChar, CancellationToken token)
        {
            return CommitBehavior.None;
        }

        async Task<CompletionContext> IAsyncCompletionItemSource.GetCompletionContextAsync(CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableSpan, CancellationToken token)
        {
            return await Task.FromResult(new CompletionContext(ImmutableArray.Create(new CompletionItem("html", this), new CompletionItem("head", this), new CompletionItem("body", this), new CompletionItem("header", this))));
        }

        async Task<object> IAsyncCompletionItemSource.GetDescriptionAsync(CompletionItem item, CancellationToken token)
        {
            return await Task.FromResult(item.DisplayText);
        }

        ImmutableArray<char> IAsyncCompletionItemSource.GetPotentialCommitCharacters()
        {
            return commitCharacters;
        }

        bool IAsyncCompletionItemSource.ShouldCommitCompletion(char typeChar, SnapshotPoint location)
        {
            return true;
        }

        SnapshotSpan? IAsyncCompletionItemSource.ShouldTriggerCompletion(char typeChar, SnapshotPoint triggerLocation)
        {
            var charBeforeCaret = triggerLocation.Subtract(1).GetChar();
            if (commitCharacters.Contains(charBeforeCaret) || triggerLocation.Position == 0)
            {
                // skip the typed character. the applicable span starts at the caret
                return new SnapshotSpan(triggerLocation, 0);
            }
            else
            {
                // include the typed character.
                return new SnapshotSpan(triggerLocation - 1, 1);
            }
        }
    }
#endif
}
