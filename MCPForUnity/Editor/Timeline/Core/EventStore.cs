using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Timeline.Context;
using UnityEditor;

namespace MCPForUnity.Editor.Timeline.Core
{
    /// <summary>
    /// Thread-safe event store for editor events.
    ///
    /// Threading model:
    /// - Writes: Main thread only (enforced by Debug.Assert in DEBUG builds)
    /// - Reads: Any thread, uses lock for snapshot pattern
    /// - Sequence generation: Uses Interlocked.Increment for atomicity
    ///
    /// Persistence: Uses McpJobStateStore for domain reload survival.
    /// Save strategy: Deferred persistence with dirty flag + delayCall coalescing.
    ///
    /// Memory optimization (Pruning):
    /// - Hot events (latest 100): Full payload retained
    /// - Cold events (older than 100): Automatically dehydrated (payload = null)
    /// - This reduces memory from ~10MB to <1MB for 1000 events
    /// </summary>
    public static class EventStore
    {
        private const int MaxEvents = 1000;
        private const int MaxContextMappings = 2000;
        private const int HotEventCount = 100;  // Keep full payload for latest 100 events
        private const string StateKey = "timeline_events";

        // Schema version for migration support
        // Increment when breaking changes are made to the storage format
        private const int CurrentSchemaVersion = 2;  // Incremented for dehydration support

        private static readonly List<EditorEvent> _events = new();
        private static readonly List<ContextMapping> _contextMappings = new();
        private static readonly object _queryLock = new();
        private static long _sequenceCounter;
        private static bool _isLoaded;
        private static bool _isDirty;  // Dirty flag for deferred persistence
        private static bool _saveScheduled;  // Prevents duplicate delayCall registrations

        // Batch notification: accumulate pending events and notify in single delayCall
        private static readonly List<EditorEvent> _pendingNotifications = new();
        private static bool _notifyScheduled;
        
        // Main thread detection: Use Unity's EditorApplication.isUpdating for runtime checks.
        // This is more robust than capturing a thread ID at static initialization time,
        // as it works correctly after domain reload and doesn't rely on managed thread IDs.
        // The MainThreadId field is kept for legacy/debugging purposes only.
        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Event raised when a new event is recorded.
        /// Used by ContextTimeline to create associations.
        /// </summary>
        public static event Action<EditorEvent> EventRecorded;

        static EventStore()
        {
            LoadFromStorage();
        }



        /// <summary>
        /// Record a new event. Must be called from main thread.
        /// Note: The main thread check uses a captured thread ID from static initialization.
        /// After domain reload, this may be re-initialized on a different managed thread.
        /// This assertion is for development debugging only.
        ///
        /// Thread safety: Write operations are protected by _queryLock for defensive
        /// programming, even though Record is expected to be called from main thread only.
        /// </summary>
        public static long Record(EditorEvent @event)
        {
            // Note: EditorApplication.isUpdating check removed because it was too strict.
            // Many valid callbacks (delayCall, AssetPostprocessor, hierarchyChanged) run
            // on the main thread but outside the update loop. The lock(_queryLock) provides
            // actual thread safety for the critical section.

            // Use Interlocked.Increment for thread-safe sequence generation
            // Even though we assert main thread, this provides defense-in-depth
            long newSequence = Interlocked.Increment(ref _sequenceCounter);

            var evtWithSequence = new EditorEvent(
                sequence: newSequence,
                timestampUnixMs: @event.TimestampUnixMs,
                type: @event.Type,
                targetId: @event.TargetId,
                payload: @event.Payload
            );

            // Write lock: protect _events and _contextMappings mutations
            lock (_queryLock)
            {
                _events.Add(evtWithSequence);

                // Auto-dehydrate old events to save memory
                // Keep latest HotEventCount events with full payload, dehydrate the rest
                if (_events.Count > HotEventCount)
                {
                    DehydrateOldEvents();
                }

                // Trim oldest events if over limit (batch remove is more efficient than loop RemoveAt)
                if (_events.Count > MaxEvents)
                {
                    int removeCount = _events.Count - MaxEvents;
                    // Capture sequences to cascade delete mappings
                    var removedSequences = new HashSet<long>();
                    for (int i = 0; i < removeCount; i++)
                    {
                        removedSequences.Add(_events[i].Sequence);
                    }
                    _events.RemoveRange(0, removeCount);

                    // Cascade delete: remove mappings for deleted events
                    _contextMappings.RemoveAll(m => removedSequences.Contains(m.EventSequence));
                }
            }

            // Mark dirty and schedule deferred save (reduces disk I/O from O(N) to O(1))
            _isDirty = true;
            ScheduleSave();

            // Batch notification: accumulate event and schedule single delayCall
            // This prevents callback queue bloat in high-frequency scenarios
            lock (_pendingNotifications)
            {
                _pendingNotifications.Add(evtWithSequence);
            }

            ScheduleNotify();

            return evtWithSequence.Sequence;
        }

        /// <summary>
        /// Query events with optional filtering.
        /// Thread-safe - can be called from any thread.
        /// Uses snapshot pattern: capture only needed range inside lock, query outside.
        ///
        /// Performance optimization: Only copies the tail portion of _events
        /// (limit + buffer) unless sinceSequence requires earlier events.
        ///
        /// Correctness guarantee: If sinceSequence is provided, ALL events with
        /// Sequence > sinceSequence are returned, even if they fall outside the
        /// normal tail window. The copy window is automatically extended to include
        /// all matching events.
        /// </summary>
        public static IReadOnlyList<EditorEvent> Query(int limit = 50, long? sinceSequence = null)
        {
            List<EditorEvent> snapshot;

            lock (_queryLock)
            {
                int count = _events.Count;
                if (count == 0)
                {
                    return Array.Empty<EditorEvent>();
                }

                // Base window: tail portion for recent queries
                int copyCount = Math.Min(count, limit + (limit / 10) + 10);
                int startIndex = count - copyCount;

                // If sinceSequence is specified, ensure we don't miss matching events
                // Find the first event with Sequence > sinceSequence and extend window if needed
                if (sinceSequence.HasValue)
                {
                    int firstMatchIndex = -1;
                    // Linear search from the end (most recent events are likely to match first)
                    for (int i = count - 1; i >= 0; i--)
                    {
                        if (_events[i].Sequence > sinceSequence.Value)
                        {
                            firstMatchIndex = i;
                        }
                        else if (firstMatchIndex >= 0)
                        {
                            // Found a non-matching event after matches, stop searching
                            break;
                        }
                    }

                    // Extend window to include all matching events
                    if (firstMatchIndex >= 0 && firstMatchIndex < startIndex)
                    {
                        startIndex = firstMatchIndex;
                        copyCount = count - startIndex;
                    }
                }

                snapshot = new List<EditorEvent>(copyCount);
                for (int i = startIndex; i < count; i++)
                {
                    snapshot.Add(_events[i]);
                }
            }

            // Query: LINQ runs outside lock, no blocking
            var query = snapshot.AsEnumerable();

            if (sinceSequence.HasValue)
            {
                query = query.Where(e => e.Sequence > sinceSequence.Value);
            }

            return query.OrderByDescending(e => e.Sequence).Take(limit).ToList();
        }

        /// <summary>
        /// Get the current sequence counter value.
        /// </summary>
        public static long CurrentSequence => _sequenceCounter;

        /// <summary>
        /// Get total event count.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (_queryLock)
                {
                    return _events.Count;
                }
            }
        }

        /// <summary>
        /// Dehydrate old events (beyond HotEventCount) to save memory.
        /// This is called automatically by Record().
        /// </summary>
        private static void DehydrateOldEvents()
        {
            // Find events that need dehydration (not already dehydrated and beyond hot count)
            for (int i = 0; i < _events.Count - HotEventCount; i++)
            {
                var evt = _events[i];
                if (evt != null && !evt.IsDehydrated && evt.Payload != null)
                {
                    // Dehydrate the event (creates new instance with Payload = null)
                    _events[i] = evt.Dehydrate();
                }
            }
        }

        /// <summary>
        /// Clear all events and context mappings (for testing).
        /// WARNING: This is destructive and cannot be undone.
        /// All timeline history and context associations will be lost.
        /// </summary>
        public static void Clear()
        {
            lock (_queryLock)
            {
                _events.Clear();
                _contextMappings.Clear();
                _sequenceCounter = 0;
            }
            // For Clear(), save immediately (not deferred) since it's a destructive operation
            SaveToStorage();
        }

        /// <summary>
        /// Clears all pending notifications and scheduled saves.
        /// Call this when shutting down or reloading domains to prevent delayCall leaks.
        /// </summary>
        public static void ClearPendingOperations()
        {
            lock (_pendingNotifications)
            {
                _pendingNotifications.Clear();
                _notifyScheduled = false;
            }
            _saveScheduled = false;
        }

        /// <summary>
        /// Get diagnostic information about memory usage.
        /// Useful for monitoring and debugging memory issues.
        /// </summary>
        public static string GetMemoryDiagnostics()
        {
            lock (_queryLock)
            {
                int totalEvents = _events.Count;
                int hotEvents = Math.Min(totalEvents, HotEventCount);
                int coldEvents = Math.Max(0, totalEvents - HotEventCount);

                int hydratedCount = 0;
                int dehydratedCount = 0;
                long estimatedPayloadBytes = 0;

                foreach (var evt in _events)
                {
                    if (evt.IsDehydrated)
                        dehydratedCount++;
                    else if (evt.Payload != null)
                    {
                        hydratedCount++;
                        // Estimate payload size (rough approximation)
                        estimatedPayloadBytes += EstimatePayloadSize(evt.Payload);
                    }
                }

                // Estimate dehydrated events size (~100 bytes each)
                long dehydratedBytes = dehydratedCount * 100;

                // Total estimated size
                long totalEstimatedBytes = estimatedPayloadBytes + dehydratedBytes;
                double totalEstimatedMB = totalEstimatedBytes / (1024.0 * 1024.0);

                return $"EventStore Memory Diagnostics:\n" +
                       $"  Total Events: {totalEvents}/{MaxEvents}\n" +
                       $"  Hot Events (full payload): {hotEvents}\n" +
                       $"  Cold Events (dehydrated): {coldEvents}\n" +
                       $"  Hydrated: {hydratedCount}\n" +
                       $"  Dehydrated: {dehydratedCount}\n" +
                       $"  Estimated Payload Memory: {estimatedPayloadBytes / 1024} KB\n" +
                       $"  Total Estimated Memory: {totalEstimatedMB:F2} MB";
            }
        }

        /// <summary>
        /// Estimate the size of a payload in bytes.
        /// This is a rough approximation for diagnostics.
        /// </summary>
        private static long EstimatePayloadSize(IReadOnlyDictionary<string, object> payload)
        {
            if (payload == null) return 0;

            long size = 0;
            foreach (var kvp in payload)
            {
                // Key string (assume average 20 chars)
                size += kvp.Key.Length * 2;

                // Value
                if (kvp.Value is string str)
                    size += str.Length * 2;
                else if (kvp.Value is int)
                    size += 4;
                else if (kvp.Value is long)
                    size += 8;
                else if (kvp.Value is double)
                    size += 8;
                else if (kvp.Value is bool)
                    size += 1;
                else if (kvp.Value is IDictionary<string, object> dict)
                    size += dict.Count * 100; // Rough estimate
                else if (kvp.Value is System.Collections.ICollection list)
                    size += list.Count * 50; // Rough estimate
                else
                    size += 50; // Unknown type
            }

            return size;
        }

        // ========================================================================
        // Context Mapping API (Phase 1)
        // ========================================================================

        /// <summary>
        /// Add a context mapping for an event.
        ///
        /// Strategy: Multiple mappings allowed for same eventSequence (different contexts).
        /// Duplicate detection: Same (eventSequence, contextId) pair will be skipped.
        /// Query behavior: QueryWithContext returns the first (most recent) mapping per event.
        ///
        /// Thread-safe - can be called from EventRecorded subscribers.
        /// </summary>
        public static void AddContextMapping(ContextMapping mapping)
        {
            lock (_queryLock)
            {
                // Skip duplicate mappings (same eventSequence and contextId)
                // This allows multiple different contexts for the same event,
                // but prevents identical duplicates from bloating the list.
                bool isDuplicate = false;
                for (int i = _contextMappings.Count - 1; i >= 0; i--)
                {
                    var existing = _contextMappings[i];
                    if (existing.EventSequence == mapping.EventSequence &&
                        existing.ContextId == mapping.ContextId)
                    {
                        isDuplicate = true;
                        break;
                    }
                    // Optimization: mappings are ordered by EventSequence
                    if (existing.EventSequence < mapping.EventSequence)
                        break;
                }

                if (isDuplicate)
                    return;

                _contextMappings.Add(mapping);

                // Trim oldest mappings if over limit
                if (_contextMappings.Count > MaxContextMappings)
                {
                    int removeCount = _contextMappings.Count - MaxContextMappings;
                    _contextMappings.RemoveRange(0, removeCount);
                }
            }

            // Mark dirty and schedule deferred save
            _isDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// Schedule a deferred save via delayCall.
        /// Multiple rapid calls result in a single save (coalesced).
        /// </summary>
        private static void ScheduleSave()
        {
            // Only schedule if not already scheduled (prevents callback queue bloat)
            if (_saveScheduled)
                return;

            _saveScheduled = true;

            // Use delayCall to coalesce multiple saves into one
            EditorApplication.delayCall += () =>
            {
                _saveScheduled = false;
                if (_isDirty)
                {
                    SaveToStorage();
                    _isDirty = false;
                }
            };
        }

        /// <summary>
        /// Schedule batch notification via delayCall.
        /// Multiple rapid events result in a single notification batch.
        /// Thread-safe: check-and-set is atomic under lock.
        /// </summary>
        private static void ScheduleNotify()
        {
            // Atomic check-and-set under lock to prevent race conditions
            lock (_pendingNotifications)
            {
                if (_notifyScheduled)
                    return;

                _notifyScheduled = true;
            }

            EditorApplication.delayCall += DrainPendingNotifications;
        }

        /// <summary>
        /// Drain all pending notifications and invoke EventRecorded for each.
        /// Called via delayCall, runs on main thread after current Record() completes.
        /// Thread-safe: clears _notifyScheduled under lock.
        /// </summary>
        private static void DrainPendingNotifications()
        {
            List<EditorEvent> toNotify;
            lock (_pendingNotifications)
            {
                _notifyScheduled = false;

                if (_pendingNotifications.Count == 0)
                    return;

                toNotify = new List<EditorEvent>(_pendingNotifications);
                _pendingNotifications.Clear();
            }

            // Notify subscribers outside lock
            foreach (var evt in toNotify)
            {
                EventRecorded?.Invoke(evt);
            }
        }

        /// <summary>
        /// Query events with their context associations.
        /// Returns a tuple of (Event, Context) where Context may be null.
        /// Note: Source filtering is not supported in Phase 1 (ContextMappings only).
        /// To filter by operation source, implement ContextStore (Phase 2).
        ///
        /// Performance optimization: Only copies the tail portion of lists
        /// unless sinceSequence requires earlier events.
        ///
        /// Correctness guarantee: If sinceSequence is provided, ALL events with
        /// Sequence > sinceSequence are returned, even if they fall outside the
        /// normal tail window.
        /// </summary>
        public static IReadOnlyList<(EditorEvent Event, ContextMapping Context)> QueryWithContext(
            int limit = 50,
            long? sinceSequence = null)
        {
            List<EditorEvent> eventsSnapshot;
            List<ContextMapping> mappingsSnapshot;

            lock (_queryLock)
            {
                int eventCount = _events.Count;
                if (eventCount == 0)
                {
                    return Array.Empty<(EditorEvent, ContextMapping)>();
                }

                // Base window: tail portion for recent queries
                int copyCount = Math.Min(eventCount, limit + (limit / 10) + 10);
                int startIndex = eventCount - copyCount;

                // If sinceSequence is specified, ensure we don't miss matching events
                if (sinceSequence.HasValue)
                {
                    int firstMatchIndex = -1;
                    for (int i = eventCount - 1; i >= 0; i--)
                    {
                        if (_events[i].Sequence > sinceSequence.Value)
                        {
                            firstMatchIndex = i;
                        }
                        else if (firstMatchIndex >= 0)
                        {
                            break;
                        }
                    }

                    if (firstMatchIndex >= 0 && firstMatchIndex < startIndex)
                    {
                        startIndex = firstMatchIndex;
                        copyCount = eventCount - startIndex;
                    }
                }

                eventsSnapshot = new List<EditorEvent>(copyCount);
                for (int i = startIndex; i < eventCount; i++)
                {
                    eventsSnapshot.Add(_events[i]);
                }

                // For mappings, copy all (usually much smaller than events)
                mappingsSnapshot = new List<ContextMapping>(_contextMappings);
            }

            // Build lookup dictionary outside lock
            var mappingBySequence = mappingsSnapshot
                .GroupBy(m => m.EventSequence)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault());

            // Query and join outside lock
            var query = eventsSnapshot.AsEnumerable();

            if (sinceSequence.HasValue)
            {
                query = query.Where(e => e.Sequence > sinceSequence.Value);
            }

            var results = query
                .OrderByDescending(e => e.Sequence)
                .Take(limit)
                .Select(e =>
                {
                    mappingBySequence.TryGetValue(e.Sequence, out var mapping);
                    // mapping may be null if no context association exists
                    return (Event: e, Context: mapping);
                })
                .ToList();

            return results;
        }

        /// <summary>
        /// Remove all context mappings for a specific context ID.
        /// </summary>
        public static void RemoveContextMappings(Guid contextId)
        {
            lock (_queryLock)
            {
                _contextMappings.RemoveAll(m => m.ContextId == contextId);
            }
            // Mark dirty and schedule deferred save
            _isDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// Get the number of stored context mappings.
        /// </summary>
        public static int ContextMappingCount
        {
            get
            {
                lock (_queryLock)
                {
                    return _contextMappings.Count;
                }
            }
        }

        private static void LoadFromStorage()
        {
            if (_isLoaded) return;

            try
            {
                var state = McpJobStateStore.LoadState<EventStoreState>(StateKey);
                if (state != null)
                {
                    // Schema version check for migration support
                    if (state.SchemaVersion > CurrentSchemaVersion)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[EventStore] Stored schema version {state.SchemaVersion} is newer " +
                            $"than current version {CurrentSchemaVersion}. Data may not load correctly. " +
                            $"Consider updating the Timeline system.");
                    }
                    else if (state.SchemaVersion < CurrentSchemaVersion)
                    {
                        // Future: Add migration logic here when schema changes
                        UnityEngine.Debug.Log(
                            $"[EventStore] Migrating data from schema version {state.SchemaVersion} to {CurrentSchemaVersion}");
                    }

                    _sequenceCounter = state.SequenceCounter;
                    _events.Clear();
                    if (state.Events != null)
                    {
                        _events.AddRange(state.Events);
                    }
                    _contextMappings.Clear();
                    if (state.ContextMappings != null)
                    {
                        _contextMappings.AddRange(state.ContextMappings);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[EventStore] Failed to load from storage: {ex.Message}");
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private static void SaveToStorage()
        {
            try
            {
                var state = new EventStoreState
                {
                    SchemaVersion = CurrentSchemaVersion,
                    SequenceCounter = _sequenceCounter,
                    Events = _events.ToList(),
                    ContextMappings = _contextMappings.ToList()
                };
                McpJobStateStore.SaveState(StateKey, state);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[EventStore] Failed to save to storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Persistent state schema for EventStore.
        /// SchemaVersion: Increment when breaking changes are made.
        /// </summary>
        private class EventStoreState
        {
            public int SchemaVersion { get; set; } = CurrentSchemaVersion;
            public long SequenceCounter { get; set; }
            public List<EditorEvent> Events { get; set; }
            public List<ContextMapping> ContextMappings { get; set; }
        }
    }
}
