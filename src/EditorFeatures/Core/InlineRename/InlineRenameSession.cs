﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Editor.Undo;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.InlineRename
{
    internal partial class InlineRenameSession : IInlineRenameSession, IFeatureController
    {
        private readonly Workspace _workspace;
        private readonly IUIThreadOperationExecutor _uiThreadOperationExecutor;
        private readonly ITextBufferAssociatedViewService _textBufferAssociatedViewService;
        private readonly ITextBufferFactoryService _textBufferFactoryService;
        private readonly IFeatureService _featureService;
        private readonly IFeatureDisableToken _completionDisabledToken;
        private readonly IEnumerable<IRefactorNotifyService> _refactorNotifyServices;
        private readonly IAsynchronousOperationListener _asyncListener;
        private readonly Solution _baseSolution;
        private readonly Document _triggerDocument;
        private readonly ITextView _triggerView;
        private readonly IDisposable _inlineRenameSessionDurationLogBlock;
        private readonly IThreadingContext _threadingContext;
        public readonly InlineRenameService RenameService;

        private bool _dismissed;
        private bool _isApplyingEdit;
        private string _replacementText;
        private SymbolRenameOptions _options;
        private bool _previewChanges;
        private readonly Dictionary<ITextBuffer, OpenTextBufferManager> _openTextBuffers = new Dictionary<ITextBuffer, OpenTextBufferManager>();

        /// <summary>
        /// If non-null, the current text of the replacement. Linked spans added will automatically be updated with this
        /// text.
        /// </summary>
        public string ReplacementText
        {
            get
            {
                return _replacementText;
            }
            private set
            {
                _replacementText = value;
                ReplacementTextChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Information about whether a file rename should be allowed as part
        /// of the rename operation, as determined by the language
        /// </summary>
        public InlineRenameFileRenameInfo FileRenameInfo { get; }

        /// <summary>
        /// The task which computes the main rename locations against the original workspace
        /// snapshot.
        /// </summary>
        private JoinableTask<IInlineRenameLocationSet> _allRenameLocationsTask;

        /// <summary>
        /// The cancellation token for most work being done by the inline rename session. This
        /// includes the <see cref="_allRenameLocationsTask"/> tasks.
        /// </summary>
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// This task is a continuation of the <see cref="_allRenameLocationsTask"/> that is the result of computing
        /// the resolutions of the rename spans for the current replacementText.
        /// </summary>
        private JoinableTask<IInlineRenameReplacementInfo> _conflictResolutionTask;

        /// <summary>
        /// The cancellation source for <see cref="_conflictResolutionTask"/>.
        /// </summary>
        private CancellationTokenSource _conflictResolutionTaskCancellationSource = new CancellationTokenSource();

        private readonly IInlineRenameInfo _renameInfo;

        /// <summary>
        /// The initial text being renamed.
        /// </summary>
        private readonly string _initialRenameText;

        public InlineRenameSession(
            IThreadingContext threadingContext,
            InlineRenameService renameService,
            Workspace workspace,
            SnapshotSpan triggerSpan,
            IInlineRenameInfo renameInfo,
            SymbolRenameOptions options,
            bool previewChanges,
            IUIThreadOperationExecutor uiThreadOperationExecutor,
            ITextBufferAssociatedViewService textBufferAssociatedViewService,
            ITextBufferFactoryService textBufferFactoryService,
            IFeatureServiceFactory featureServiceFactory,
            IEnumerable<IRefactorNotifyService> refactorNotifyServices,
            IAsynchronousOperationListener asyncListener)
        {
            // This should always be touching a symbol since we verified that upon invocation
            _threadingContext = threadingContext;
            _renameInfo = renameInfo;

            _triggerDocument = triggerSpan.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (_triggerDocument == null)
            {
                throw new InvalidOperationException(EditorFeaturesResources.The_triggerSpan_is_not_included_in_the_given_workspace);
            }

            _inlineRenameSessionDurationLogBlock = Logger.LogBlock(FunctionId.Rename_InlineSession, CancellationToken.None);

            _workspace = workspace;
            _workspace.WorkspaceChanged += OnWorkspaceChanged;

            _textBufferFactoryService = textBufferFactoryService;
            _textBufferAssociatedViewService = textBufferAssociatedViewService;
            _textBufferAssociatedViewService.SubjectBuffersConnected += OnSubjectBuffersConnected;

            // Disable completion when an inline rename session starts
            _featureService = featureServiceFactory.GlobalFeatureService;
            _completionDisabledToken = _featureService.Disable(PredefinedEditorFeatureNames.Completion, this);
            RenameService = renameService;
            _uiThreadOperationExecutor = uiThreadOperationExecutor;
            _refactorNotifyServices = refactorNotifyServices;
            _asyncListener = asyncListener;
            _triggerView = textBufferAssociatedViewService.GetAssociatedTextViews(triggerSpan.Snapshot.TextBuffer).FirstOrDefault(v => v.HasAggregateFocus) ??
                textBufferAssociatedViewService.GetAssociatedTextViews(triggerSpan.Snapshot.TextBuffer).First();

            _options = options;
            _previewChanges = previewChanges;

            _initialRenameText = triggerSpan.GetText();
            this.ReplacementText = _initialRenameText;

            _baseSolution = _triggerDocument.Project.Solution;
            this.UndoManager = workspace.Services.GetService<IInlineRenameUndoManager>();

            if (_renameInfo is IInlineRenameInfoWithFileRename renameInfoWithFileRename)
            {
                FileRenameInfo = renameInfoWithFileRename.GetFileRenameInfo();
            }
            else
            {
                FileRenameInfo = InlineRenameFileRenameInfo.NotAllowed;
            }

            InitializeOpenBuffers(triggerSpan);
        }

        public string OriginalSymbolName => _renameInfo.DisplayName;

        // Used to aid the investigation of https://github.com/dotnet/roslyn/issues/7364
        private class NullTextBufferException : Exception
        {
#pragma warning disable IDE0052 // Remove unread private members
            private readonly Document _document;
            private readonly SourceText _text;
#pragma warning restore IDE0052 // Remove unread private members

            public NullTextBufferException(Document document, SourceText text)
                : base("Cannot retrieve textbuffer from document.")
            {
                _document = document;
                _text = text;
            }
        }

        private void InitializeOpenBuffers(SnapshotSpan triggerSpan)
        {
            using (Logger.LogBlock(FunctionId.Rename_CreateOpenTextBufferManagerForAllOpenDocs, CancellationToken.None))
            {
                var openBuffers = new HashSet<ITextBuffer>();
                foreach (var d in _workspace.GetOpenDocumentIds())
                {
                    var document = _baseSolution.GetDocument(d);
                    if (document == null)
                    {
                        continue;
                    }

                    Contract.ThrowIfFalse(document.TryGetText(out var text));
                    Contract.ThrowIfNull(text);

                    var textSnapshot = text.FindCorrespondingEditorTextSnapshot();
                    if (textSnapshot == null)
                    {
                        FatalError.ReportAndCatch(new NullTextBufferException(document, text));
                        continue;
                    }

                    Contract.ThrowIfNull(textSnapshot.TextBuffer);

                    openBuffers.Add(textSnapshot.TextBuffer);
                }

                foreach (var buffer in openBuffers)
                {
                    TryPopulateOpenTextBufferManagerForBuffer(buffer);
                }
            }

            var startingSpan = triggerSpan.Span;

            // Select this span if we didn't already have something selected
            var selections = _triggerView.Selection.GetSnapshotSpansOnBuffer(triggerSpan.Snapshot.TextBuffer);
            if (!selections.Any() ||
                selections.First().IsEmpty ||
                !startingSpan.Contains(selections.First()))
            {
                _triggerView.SetSelection(new SnapshotSpan(triggerSpan.Snapshot, startingSpan));
            }

            this.UndoManager.CreateInitialState(this.ReplacementText, _triggerView.Selection, new SnapshotSpan(triggerSpan.Snapshot, startingSpan));
            _openTextBuffers[triggerSpan.Snapshot.TextBuffer].SetReferenceSpans(SpecializedCollections.SingletonEnumerable(startingSpan.ToTextSpan()));

            UpdateReferenceLocationsTask(_threadingContext.JoinableTaskFactory.RunAsync(
                () => _renameInfo.FindRenameLocationsAsync(_options, _cancellationTokenSource.Token)));

            RenameTrackingDismisser.DismissRenameTracking(_workspace, _workspace.GetOpenDocumentIds());
        }

        private bool TryPopulateOpenTextBufferManagerForBuffer(ITextBuffer buffer)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();

            if (_workspace.Kind == WorkspaceKind.Interactive)
            {
                Debug.Assert(buffer.GetRelatedDocuments().Count() == 1);
                Debug.Assert(buffer.IsReadOnly(0) == buffer.IsReadOnly(VisualStudio.Text.Span.FromBounds(0, buffer.CurrentSnapshot.Length))); // All or nothing.
                if (buffer.IsReadOnly(0))
                {
                    return false;
                }
            }

            if (!_openTextBuffers.ContainsKey(buffer) && buffer.SupportsRename())
            {
                _openTextBuffers[buffer] = new OpenTextBufferManager(this, buffer, _workspace, _textBufferFactoryService);
                return true;
            }

            return _openTextBuffers.ContainsKey(buffer);
        }

        private void OnSubjectBuffersConnected(object sender, SubjectBuffersConnectedEventArgs e)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            foreach (var buffer in e.SubjectBuffers)
            {
                if (buffer.GetWorkspace() == _workspace)
                {
                    if (TryPopulateOpenTextBufferManagerForBuffer(buffer))
                    {
                        _openTextBuffers[buffer].ConnectToView(e.TextView);
                    }
                }
            }
        }

        private void UpdateReferenceLocationsTask(JoinableTask<IInlineRenameLocationSet> findRenameLocationsTask)
        {
            _threadingContext.ThrowIfNotOnUIThread();

            var asyncToken = _asyncListener.BeginAsyncOperation("UpdateReferencesTask");
            _allRenameLocationsTask = _threadingContext.JoinableTaskFactory.RunAsync(async () =>
            {
                var inlineRenameLocations = await findRenameLocationsTask.JoinAsync().ConfigureAwaitRunInline();

                // It's unfortunate that _allRenameLocationsTask has a UI thread dependency (prevents continuations
                // from running prior to the completion of the UI operation), but the implementation does not currently
                // follow the originally-intended design.
                // https://github.com/dotnet/roslyn/issues/40890
                await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true, _cancellationTokenSource.Token);

                RaiseSessionSpansUpdated(inlineRenameLocations.Locations.ToImmutableArray());

                return inlineRenameLocations;
            });

            _allRenameLocationsTask.Task.CompletesAsyncOperation(asyncToken);

            UpdateConflictResolutionTask();
            QueueApplyReplacements();
        }

        public Workspace Workspace => _workspace;
        public SymbolRenameOptions Options => _options;
        public bool PreviewChanges => _previewChanges;
        public bool HasRenameOverloads => _renameInfo.HasOverloads;
        public bool MustRenameOverloads => _renameInfo.MustRenameOverloads;

        public IInlineRenameUndoManager UndoManager { get; }

        public event EventHandler<ImmutableArray<InlineRenameLocation>> ReferenceLocationsChanged;
        public event EventHandler<IInlineRenameReplacementInfo> ReplacementsComputed;
        public event EventHandler ReplacementTextChanged;

        internal OpenTextBufferManager GetBufferManager(ITextBuffer buffer)
            => _openTextBuffers[buffer];

        internal bool TryGetBufferManager(ITextBuffer buffer, out OpenTextBufferManager bufferManager)
            => _openTextBuffers.TryGetValue(buffer, out bufferManager);

        public void RefreshRenameSessionWithOptionsChanged(SymbolRenameOptions newOptions)
        {
            if (_options == newOptions)
            {
                return;
            }

            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();

            _options = newOptions;

            var cancellationToken = _cancellationTokenSource.Token;

            UpdateReferenceLocationsTask(_threadingContext.JoinableTaskFactory.RunAsync(async () =>
            {
                // Join prior work before proceeding, since it performs a required state update.
                // https://github.com/dotnet/roslyn/pull/34254#discussion_r267024593
                //
                // The cancellation token is passed to the prior work when it starts, not when it's joined. This is
                // the equivalent of TaskContinuationOptions.LazyCancellation.
                await _allRenameLocationsTask.JoinAsync(CancellationToken.None).ConfigureAwait(false);
                await TaskScheduler.Default;

                return await _renameInfo.FindRenameLocationsAsync(_options, cancellationToken).ConfigureAwait(false);
            }));
        }

        public void SetPreviewChanges(bool value)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();

            _previewChanges = value;
        }

        private void VerifyNotDismissed()
        {
            if (_dismissed)
            {
                throw new InvalidOperationException(EditorFeaturesResources.This_session_has_already_been_dismissed);
            }
        }

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs args)
        {
            if (args.Kind != WorkspaceChangeKind.DocumentChanged)
            {
                if (!_dismissed)
                {
                    this.Cancel();
                }
            }
        }

        private void RaiseSessionSpansUpdated(ImmutableArray<InlineRenameLocation> locations)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            SetReferenceLocations(locations);

            // It's OK to call SetReferenceLocations with all documents, including unchangeable ones,
            // because they can't be opened, so the _openTextBuffers loop won't matter. In fact, the entire
            // inline rename is oblivious to unchangeable documents, we just need to filter out references
            // in them to avoid displaying them in the UI.
            // https://github.com/dotnet/roslyn/issues/41242
            if (_workspace.IgnoreUnchangeableDocumentsWhenApplyingChanges)
            {
                locations = locations.WhereAsArray(l => l.Document.CanApplyChange());
            }

            ReferenceLocationsChanged?.Invoke(this, locations);
        }

        private void SetReferenceLocations(ImmutableArray<InlineRenameLocation> locations)
        {
            _threadingContext.ThrowIfNotOnUIThread();

            var locationsByDocument = locations.ToLookup(l => l.Document.Id);

            _isApplyingEdit = true;
            foreach (var textBuffer in _openTextBuffers.Keys)
            {
                var documents = textBuffer.AsTextContainer().GetRelatedDocuments();

                if (!documents.Any(d => locationsByDocument.Contains(d.Id)))
                {
                    _openTextBuffers[textBuffer].SetReferenceSpans(SpecializedCollections.EmptyEnumerable<TextSpan>());
                }
                else
                {
                    var spans = documents.SelectMany(d => locationsByDocument[d.Id]).Select(l => l.TextSpan).Distinct();
                    _openTextBuffers[textBuffer].SetReferenceSpans(spans);
                }
            }

            _isApplyingEdit = false;
        }

        /// <summary>
        /// Updates the replacement text for the rename session and propagates it to all live buffers.
        /// </summary>
        internal void ApplyReplacementText(string replacementText, bool propagateEditImmediately)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();
            this.ReplacementText = _renameInfo.GetFinalSymbolName(replacementText);

            var asyncToken = _asyncListener.BeginAsyncOperation(nameof(ApplyReplacementText));

            Action propagateEditAction = delegate
            {
                _threadingContext.ThrowIfNotOnUIThread();

                if (_dismissed)
                {
                    asyncToken.Dispose();
                    return;
                }

                _isApplyingEdit = true;
                using (Logger.LogBlock(FunctionId.Rename_ApplyReplacementText, replacementText, _cancellationTokenSource.Token))
                {
                    foreach (var openBuffer in _openTextBuffers.Values)
                    {
                        openBuffer.ApplyReplacementText();
                    }
                }

                _isApplyingEdit = false;

                // We already kicked off UpdateConflictResolutionTask below (outside the delegate).
                // Now that we are certain the replacement text has been propagated to all of the
                // open buffers, it is safe to actually apply the replacements it has calculated.
                // See https://devdiv.visualstudio.com/DevDiv/_workitems?_a=edit&id=227513
                QueueApplyReplacements();

                asyncToken.Dispose();
            };

            // Start the conflict resolution task but do not apply the results immediately. The
            // buffer changes performed in propagateEditAction can cause source control modal
            // dialogs to show. Those dialogs pump, and yield the UI thread to whatever work is
            // waiting to be done there, including our ApplyReplacements work. If ApplyReplacements
            // starts running on the UI thread while propagateEditAction is still updating buffers
            // on the UI thread, we crash because we try to enumerate the undo stack while an undo
            // transaction is still in process. Therefore, we defer QueueApplyReplacements until
            // after the buffers have been edited, and any modal dialogs have been completed.
            // In addition to avoiding the crash, this also ensures that the resolved conflict text
            // is applied after the simple text change is propagated.
            // See https://devdiv.visualstudio.com/DevDiv/_workitems?_a=edit&id=227513
            UpdateConflictResolutionTask();

            if (propagateEditImmediately)
            {
                propagateEditAction();
            }
            else
            {
                // When responding to a text edit, we delay propagating the edit until the first transaction completes.
                _threadingContext.JoinableTaskFactory.RunAsync(async () =>
                {
                    await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                    propagateEditAction();
                });
            }
        }

        private void UpdateConflictResolutionTask()
        {
            _threadingContext.ThrowIfNotOnUIThread();

            _conflictResolutionTaskCancellationSource.Cancel();
            _conflictResolutionTaskCancellationSource = new CancellationTokenSource();

            // If the replacement text is empty, we do not update the results of the conflict
            // resolution task. We instead wait for a non-empty identifier.
            if (this.ReplacementText == string.Empty)
            {
                return;
            }

            var replacementText = this.ReplacementText;
            var options = _options;
            var cancellationToken = _conflictResolutionTaskCancellationSource.Token;

            var asyncToken = _asyncListener.BeginAsyncOperation(nameof(UpdateConflictResolutionTask));

            _conflictResolutionTask = _threadingContext.JoinableTaskFactory.RunAsync(async () =>
            {
                // Join prior work before proceeding, since it performs a required state update.
                // https://github.com/dotnet/roslyn/pull/34254#discussion_r267024593
                //
                // If cancellation of the conflict resolution task is requested before the rename locations task
                // completes, we do not need to wait for rename before cancelling. The next conflict resolution task
                // will wait on the latest rename location task if/when necessary.
                var result = await _allRenameLocationsTask.JoinAsync(cancellationToken).ConfigureAwait(false);
                await TaskScheduler.Default;

                return await result.GetReplacementsAsync(replacementText, options, cancellationToken).ConfigureAwait(false);
            });

            _conflictResolutionTask.Task.CompletesAsyncOperation(asyncToken);
        }

        [SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "False positive in methods using JTF: https://github.com/dotnet/roslyn-analyzers/issues/4283")]
        private void QueueApplyReplacements()
        {
            // If the replacement text is empty, we do not update the results of the conflict
            // resolution task. We instead wait for a non-empty identifier.
            if (this.ReplacementText == string.Empty)
            {
                return;
            }

            var cancellationToken = _conflictResolutionTaskCancellationSource.Token;
            var asyncToken = _asyncListener.BeginAsyncOperation(nameof(QueueApplyReplacements));
            var replacementOperation = _threadingContext.JoinableTaskFactory.RunAsync(async () =>
            {
                var replacementInfo = await _conflictResolutionTask.JoinAsync(CancellationToken.None).ConfigureAwait(false);
                if (replacementInfo == null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Switch to a background thread for expensive work
                await TaskScheduler.Default;
                var computedMergeResult = await ComputeMergeResultAsync(replacementInfo, cancellationToken);
                await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true, cancellationToken);
                ApplyReplacements(computedMergeResult.replacementInfo, computedMergeResult.mergeResult, cancellationToken);
            });
            replacementOperation.Task.CompletesAsyncOperation(asyncToken);
        }

        private async Task<(IInlineRenameReplacementInfo replacementInfo, LinkedFileMergeSessionResult mergeResult)> ComputeMergeResultAsync(IInlineRenameReplacementInfo replacementInfo, CancellationToken cancellationToken)
        {
            var diffMergingSession = new LinkedFileDiffMergingSession(_baseSolution, replacementInfo.NewSolution, replacementInfo.NewSolution.GetChanges(_baseSolution));
            var mergeResult = await diffMergingSession.MergeDiffsAsync(mergeConflictHandler: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (replacementInfo, mergeResult);
        }

        private void ApplyReplacements(IInlineRenameReplacementInfo replacementInfo, LinkedFileMergeSessionResult mergeResult, CancellationToken cancellationToken)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            cancellationToken.ThrowIfCancellationRequested();

            RaiseReplacementsComputed(replacementInfo);

            _isApplyingEdit = true;
            foreach (var textBuffer in _openTextBuffers.Keys)
            {
                var documents = textBuffer.CurrentSnapshot.GetRelatedDocumentsWithChanges();
                if (documents.Any())
                {
                    var textBufferManager = _openTextBuffers[textBuffer];
                    textBufferManager.ApplyConflictResolutionEdits(replacementInfo, mergeResult, documents, cancellationToken);
                }
            }

            _isApplyingEdit = false;
        }

        private void RaiseReplacementsComputed(IInlineRenameReplacementInfo resolution)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            ReplacementsComputed?.Invoke(this, resolution);
        }

        private void LogRenameSession(RenameLogMessage.UserActionOutcome outcome, bool previewChanges)
        {
            if (_conflictResolutionTask == null)
            {
                return;
            }

            var conflictResolutionFinishedComputing = _conflictResolutionTask.Task.Status == TaskStatus.RanToCompletion;

            if (conflictResolutionFinishedComputing)
            {
                var result = _conflictResolutionTask.Task.Result;
                var replacementKinds = result.GetAllReplacementKinds().ToList();

                Logger.Log(FunctionId.Rename_InlineSession_Session, RenameLogMessage.Create(
                    _options,
                    outcome,
                    conflictResolutionFinishedComputing,
                    previewChanges,
                    replacementKinds));
            }
            else
            {
                Debug.Assert(outcome.HasFlag(RenameLogMessage.UserActionOutcome.Canceled));
                Logger.Log(FunctionId.Rename_InlineSession_Session, RenameLogMessage.Create(
                    _options,
                    outcome,
                    conflictResolutionFinishedComputing,
                    previewChanges,
                    SpecializedCollections.EmptyList<InlineRenameReplacementKind>()));
            }
        }

        public void Cancel()
        {
            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();

            DismissUIAndRollbackEditsAndEndRenameSession(RenameLogMessage.UserActionOutcome.Canceled, previewChanges: false);
        }

        private void DismissUIAndRollbackEditsAndEndRenameSession(
            RenameLogMessage.UserActionOutcome outcome,
            bool previewChanges,
            Action finalCommitAction = null)
        {
            // Note: this entire sequence of steps is not cancellable.  We must perform it all to get back to a correct
            // state for all the editors the user is interacting with.

            // Remove all our adornments and restore all buffer texts to their initial state.
            DismissUIAndRollbackEdits();

            _triggerView.Caret.PositionChanged += LogPositionChanged;

            // We're about to perform the final commit action.  No need to do any of our BG work to find-refs or compute conflicts.
            _cancellationTokenSource.Cancel();
            _conflictResolutionTaskCancellationSource.Cancel();

            // Perform the actual commit step if we've been asked to.
            finalCommitAction?.Invoke();

            // Log the result so we know how well rename is going in practice.
            LogRenameSession(outcome, previewChanges);

            // Remove all our rename trackers from the text buffer properties.
            RenameTrackingDismisser.DismissRenameTracking(_workspace, _workspace.GetOpenDocumentIds());

            // Log how long the full rename took.
            _inlineRenameSessionDurationLogBlock.Dispose();

            _triggerView.Caret.PositionChanged -= LogPositionChanged;

            return;

            void DismissUIAndRollbackEdits()
            {
                _dismissed = true;
                _workspace.WorkspaceChanged -= OnWorkspaceChanged;
                _textBufferAssociatedViewService.SubjectBuffersConnected -= OnSubjectBuffersConnected;

                // Reenable completion now that the inline rename session is done
                _completionDisabledToken.Dispose();

                foreach (var textBuffer in _openTextBuffers.Keys)
                {
                    var document = textBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
                    var isClosed = document == null;

                    var openBuffer = _openTextBuffers[textBuffer];
                    openBuffer.DisconnectAndRollbackEdits(isClosed);
                }

                this.UndoManager.Disconnect();

                if (_triggerView != null && !_triggerView.IsClosed)
                {
                    _triggerView.Selection.Clear();
                }

                RenameService.ActiveSession = null;
            }

            void LogPositionChanged(object sender, CaretPositionChangedEventArgs e)
            {
                try
                {
                    throw new InvalidOperationException("Caret position changed during application of rename");
                }
                catch (InvalidOperationException ex) when (FatalError.ReportAndCatch(ex))
                {
                }
            }
        }

        public void Commit(bool previewChanges = false)
            => CommitWorker(previewChanges);

        /// <returns><see langword="true"/> if the rename operation was commited, <see
        /// langword="false"/> otherwise</returns>
        private bool CommitWorker(bool previewChanges)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            VerifyNotDismissed();

            // If the identifier was deleted (or didn't change at all) then cancel the operation.
            // Note: an alternative approach would be for the work we're doing (like detecting
            // conflicts) to quickly bail in the case of no change.  However, that involves deeper
            // changes to the system and is less easy to validate that nothing happens.
            //
            // The only potential downside here would be if there was a language that wanted to
            // still 'rename' even if the identifier went away (or was unchanged).  But that isn't
            // a case we're aware of, so it's fine to be opinionated here that we can quickly bail
            // in these cases.
            if (this.ReplacementText == string.Empty ||
                this.ReplacementText == _initialRenameText)
            {
                Cancel();
                return false;
            }

            previewChanges = previewChanges || _previewChanges;

            var result = _uiThreadOperationExecutor.Execute(
                title: EditorFeaturesResources.Rename,
                defaultDescription: EditorFeaturesResources.Computing_Rename_information,
                allowCancellation: true,
                showProgress: false,
                action: context => CommitCore(context, previewChanges));

            if (result == UIThreadOperationStatus.Canceled)
            {
                DismissUIAndRollbackEditsAndEndRenameSession(
                    RenameLogMessage.UserActionOutcome.Canceled | RenameLogMessage.UserActionOutcome.Committed, previewChanges);
                return false;
            }

            return true;
        }

        private void CommitCore(IUIThreadOperationContext operationContext, bool previewChanges)
        {
            var eventName = previewChanges ? FunctionId.Rename_CommitCoreWithPreview : FunctionId.Rename_CommitCore;
            using (Logger.LogBlock(eventName, KeyValueLogMessage.Create(LogType.UserAction), operationContext.UserCancellationToken))
            {
                var newSolution = _conflictResolutionTask.Join(operationContext.UserCancellationToken).NewSolution;

                if (previewChanges)
                {
                    var previewService = _workspace.Services.GetService<IPreviewDialogService>();

                    operationContext.TakeOwnership();
                    newSolution = previewService.PreviewChanges(
                        string.Format(EditorFeaturesResources.Preview_Changes_0, EditorFeaturesResources.Rename),
                        "vs.csharp.refactoring.rename",
                        string.Format(EditorFeaturesResources.Rename_0_to_1_colon, this.OriginalSymbolName, this.ReplacementText),
                        _renameInfo.FullDisplayName,
                        _renameInfo.Glyph,
                        newSolution,
                        _triggerDocument.Project.Solution);

                    if (newSolution == null)
                    {
                        // User clicked cancel.
                        return;
                    }
                }

                // The user hasn't canceled by now, so we're done waiting for them. Off to rename!
                using var _ = operationContext.AddScope(allowCancellation: false, EditorFeaturesResources.Updating_files);

                DismissUIAndRollbackEditsAndEndRenameSession(
                    RenameLogMessage.UserActionOutcome.Committed, previewChanges,
                    () => ApplyRename(newSolution, operationContext));
            }
        }

        private void ApplyRename(Solution newSolution, IUIThreadOperationContext operationContext)
        {
            var changes = _baseSolution.GetChanges(newSolution);
            var changedDocumentIDs = changes.GetProjectChanges().SelectMany(c => c.GetChangedDocuments()).ToList();

            if (!_renameInfo.TryOnBeforeGlobalSymbolRenamed(_workspace, changedDocumentIDs, this.ReplacementText))
            {
                var notificationService = _workspace.Services.GetService<INotificationService>();
                notificationService.SendNotification(
                    EditorFeaturesResources.Rename_operation_was_cancelled_or_is_not_valid,
                    EditorFeaturesResources.Rename_Symbol,
                    NotificationSeverity.Error);
                return;
            }

            using var undoTransaction = _workspace.OpenGlobalUndoTransaction(EditorFeaturesResources.Inline_Rename);
            var finalSolution = newSolution.Workspace.CurrentSolution;
            foreach (var id in changedDocumentIDs)
            {
                // If the document supports syntax tree, then create the new solution from the
                // updated syntax root.  This should ensure that annotations are preserved, and
                // prevents the solution from having to reparse documents when we already have
                // the trees for them.  If we don't support syntax, then just use the text of
                // the document.
                var newDocument = newSolution.GetDocument(id);

                if (newDocument.SupportsSyntaxTree)
                {
                    // We pass CancellationToken.None here because we don't have a usable token to pass. The IUIThreadOperationContext
                    // passed here as a cancellation token, but the caller in CommitCore has already turned off cancellation
                    // because we're committed to the update at this point. If we ever want to pass cancellation here, we'd want to move this
                    // part back out of this method and before the point where we've already opened a global transaction.
                    var root = newDocument.GetSyntaxRootSynchronously(CancellationToken.None);
                    finalSolution = finalSolution.WithDocumentSyntaxRoot(id, root);
                }
                else
                {
                    var newText = newDocument.GetTextSynchronously(CancellationToken.None);
                    finalSolution = finalSolution.WithDocumentText(id, newText);
                }

                // Make sure to include any document rename as well
                finalSolution = finalSolution.WithDocumentName(id, newDocument.Name);
            }

            if (_workspace.TryApplyChanges(finalSolution))
            {
                // Since rename can apply file changes as well, and those file
                // changes can generate new document ids, include added documents
                // as well as changed documents. This also ensures that any document
                // that was removed is not included
                var finalChanges = _workspace.CurrentSolution.GetChanges(_baseSolution);

                var finalChangedIds = finalChanges
                        .GetProjectChanges()
                        .SelectMany(c => c.GetChangedDocuments().Concat(c.GetAddedDocuments()))
                        .ToList();

                if (!_renameInfo.TryOnAfterGlobalSymbolRenamed(_workspace, finalChangedIds, this.ReplacementText))
                {
                    var notificationService = _workspace.Services.GetService<INotificationService>();
                    operationContext.TakeOwnership();
                    notificationService.SendNotification(
                        EditorFeaturesResources.Rename_operation_was_not_properly_completed_Some_file_might_not_have_been_updated,
                        EditorFeaturesResources.Rename_Symbol,
                        NotificationSeverity.Information);
                }

                undoTransaction.Commit();
            }
        }

        internal bool TryGetContainingEditableSpan(SnapshotPoint point, out SnapshotSpan editableSpan)
        {
            editableSpan = default;
            if (!_openTextBuffers.TryGetValue(point.Snapshot.TextBuffer, out var bufferManager))
            {
                return false;
            }

            foreach (var span in bufferManager.GetEditableSpansForSnapshot(point.Snapshot))
            {
                if (span.Contains(point) || span.End == point)
                {
                    editableSpan = span;
                    return true;
                }
            }

            return false;
        }

        internal bool IsInOpenTextBuffer(SnapshotPoint point)
            => _openTextBuffers.ContainsKey(point.Snapshot.TextBuffer);

        internal TestAccessor GetTestAccessor()
            => new TestAccessor(this);

        public struct TestAccessor
        {
            private readonly InlineRenameSession _inlineRenameSession;

            public TestAccessor(InlineRenameSession inlineRenameSession)
                => _inlineRenameSession = inlineRenameSession;

            public bool CommitWorker(bool previewChanges)
                => _inlineRenameSession.CommitWorker(previewChanges);
        }
    }
}
