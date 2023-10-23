﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Editor.Tagging;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;

namespace Microsoft.CodeAnalysis.Classification;

[Export(typeof(IViewTaggerProvider))]
[TagType(typeof(IClassificationTag))]
[Microsoft.VisualStudio.Utilities.ContentType(ContentTypeNames.RoslynContentType)]
internal sealed class TotalClassificationTaggerProvider : IViewTaggerProvider
{
    private readonly SyntacticClassificationTaggerProvider _syntacticTaggerProvider;
    private readonly SemanticClassificationViewTaggerProvider _semanticTaggerProvider;
    private readonly EmbeddedLanguageClassificationViewTaggerProvider _embeddedTaggerProvider;

    [ImportingConstructor]
    [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification = "Used in test code: https://github.com/dotnet/roslyn/issues/42814")]
    public TotalClassificationTaggerProvider(
        IThreadingContext threadingContext,
        ClassificationTypeMap typeMap,
        IGlobalOptionService globalOptions,
        [Import(AllowDefault = true)] ITextBufferVisibilityTracker? visibilityTracker,
        IAsynchronousOperationListenerProvider listenerProvider)
    {
        _syntacticTaggerProvider = new(threadingContext, typeMap, globalOptions, listenerProvider);
        _semanticTaggerProvider = new(threadingContext, typeMap, globalOptions, visibilityTracker, listenerProvider);
        _embeddedTaggerProvider = new(threadingContext, typeMap, globalOptions, visibilityTracker, listenerProvider);
    }

    public ITagger<T>? CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
    {
        var syntacticTagger = _syntacticTaggerProvider.CreateTagger<T>(buffer);
        var semanticTagger = _semanticTaggerProvider.CreateTagger(textView, buffer);
        var embeddedTagger = _embeddedTaggerProvider.CreateTagger(textView, buffer);

        if (syntacticTagger is not ITagger<IClassificationTag> typedSyntacticTagger)
        {
            (syntacticTagger as IDisposable)?.Dispose();
            semanticTagger.Dispose();
            embeddedTagger.Dispose();
            return null;
        }

        var finalTagger = new TotalClassificationAggregateTagger(typedSyntacticTagger, semanticTagger, embeddedTagger);
        if (finalTagger is not ITagger<T> typedTagger)
        {
            finalTagger.Dispose();
            return null;
        }

        return typedTagger;
    }

    private sealed class TotalClassificationAggregateTagger(
        ITagger<IClassificationTag> syntacticTagger,
        SimpleAggregateTagger<IClassificationTag> semanticTagger,
        SimpleAggregateTagger<IClassificationTag> embeddedTagger)
        : AbstractAggregateTagger<IClassificationTag>(ImmutableArray.Create(syntacticTagger, semanticTagger, embeddedTagger))
    {
        private static readonly Comparison<ITagSpan<IClassificationTag>> s_spanComparison = static (s1, s2) => s1.Span.Start - s2.Span.Start;

        public override IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            // First, get all the syntactic tags.  While they are generally overridden by semantic tags (since semantics
            // allows us to understand better what things like identifiers mean), they do take precedence for certain
            // tags like 'Comments' and 'Excluded Code'.  In those cases we want the classification to 'snap' instantly to
            // the syntactic state, and we do not want things like semantic classifications showing up over that.

            var totalTags = new SegmentedList<ITagSpan<IClassificationTag>>();

            var syntacticSpans = syntacticTagger.GetTags(spans).GetEnumerator();
            var semanticSpans = semanticTagger.GetTags(spans).GetEnumerator();

            //var syntacticIntervalTree = SimpleIntervalTree.Create(TagSpanIntrospector.Instance, syntacticTagger.GetTags(spans));
            //var semanticIntervalTree = SimpleIntervalTree.Create(TagSpanIntrospector.Instance, semanticTagger.GetTags(spans));

            var currentSyntactic = NextOrNull(syntacticSpans);
            var currentSemantic = NextOrNull(semanticSpans);

            while (currentSyntactic != null && currentSemantic != null)
            {
                // as long as we see semantic spans before the next syntactic one, keep adding them.
                if (currentSemantic.Span.Start <= currentSyntactic.Span.Start)
                {
                    totalTags.Add(currentSemantic);
                    currentSemantic = NextOrNull(semanticSpans);
                    continue;
                }

                if (currentSyntactic.Tag.ClassificationType.Classification is ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral)
                {
                    // If we have a string literal of some sort, see if there are embedded classifications within it.
                    var embeddedClassifications = embeddedTagger.GetTags(new NormalizedSnapshotSpanCollection(currentSyntactic.Span));
                    MergeEmbeddedClassifications(currentSyntactic, embeddedClassifications, totalTags);
                    currentSyntactic = NextOrNull(syntacticSpans);
                    continue;
                }

                // Otherwise, we've got a syntactic span starting before a semantic one.  If it's a comment or excluded
                // code, then we want to ignore every semantic classification that potentially overlaps with it so that
                // semantic classifications don't show up *on top of* them.  We want commenting out code to feel like'
                // it instantly snaps to that state.
                if (currentSyntactic.Tag.ClassificationType.Classification is ClassificationTypeNames.Comment or ClassificationTypeNames.ExcludedCode)
                {
                    // Keep skipping semantic tags that overlaps with this syntactic tag.
                    while (currentSemantic != null && currentSemantic.Span.OverlapsWith(currentSyntactic.Span.Span))
                        currentSemantic = NextOrNull(semanticSpans);
                }

                // now add that syntactic span.
                totalTags.Add(currentSyntactic);
                currentSyntactic = NextOrNull(syntacticSpans);
            }

            while (currentSemantic != null)
            {
                totalTags.Add(currentSemantic);
                currentSemantic = NextOrNull(semanticSpans);
                continue;
            }

            while (currentSyntactic != null)
            {

            }

            //while (latest != null)
            //{
            //    added.Add(latest.Span);
            //    latest = NextOrNull(latestEnumerator);
            //}

            //while (previous != null)
            //{
            //    removed.Add(previous.Span);
            //    previous = NextOrNull(previousEnumerator);
            //}
        }

        //private readonly struct TagSpanIntrospector : IIntervalIntrospector<ITagSpan<TTag>>
        //{
        //    public static readonly TagSpanIntrospector Instance = new();

        //    private TagSpanIntrospector()
        //    {
        //    }

        //    public int GetStart(ITagSpan<TTag> value)
        //        => value.Span.Start;

        //    public int GetLength(ITagSpan<TTag> value)
        //        => value.Span.Length;
        //}
    }

    private static ITagSpan<IClassificationTag>? NextOrNull(IEnumerator<ITagSpan<IClassificationTag>> enumerator)
        => enumerator.MoveNext() ? enumerator.Current : null;

    private static ITagSpan<IClassificationTag>? NextOrNull(SegmentedList<ITagSpan<IClassificationTag>>.Enumerator enumerator)
        => enumerator.MoveNext() ? enumerator.Current : null;
}
