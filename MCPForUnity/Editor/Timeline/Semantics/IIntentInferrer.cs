using System.Collections.Generic;
using MCPForUnity.Editor.Timeline.Core;

namespace MCPForUnity.Editor.Timeline.Semantics
{
    /// <summary>
    /// Intent inference interface.
    /// Analyzes events to infer the user's intent or purpose.
    /// Intents are computed at query time using surrounding event context.
    /// </summary>
    public interface IIntentInferrer
    {
        /// <summary>
        /// Infer the intent behind an event.
        /// May analyze surrounding events to determine context.
        /// </summary>
        /// <param name="evt">The event to analyze</param>
        /// <param name="surrounding">Surrounding events for context (may be empty)</param>
        /// <returns>Inferred intent description, or null if unable to infer</returns>
        string Infer(EditorEvent evt, IReadOnlyList<EditorEvent> surrounding);
    }
}
