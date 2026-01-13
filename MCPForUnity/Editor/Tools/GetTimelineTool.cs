using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Timeline;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for querying the timeline of editor events.
    ///
    /// This is a convenience wrapper around TimelineViewResource that provides
    /// a cleaner "get_timeline" tool name for AI consumption.
    ///
    /// Parameters:
    ///   - limit: Maximum number of events to return (1-1000, default: 50)
    ///   - since_sequence: Only return events after this sequence number
    ///   - include_context: Include context associations (default: false)
    ///   - include_semantics: Include importance, category, intent (default: false)
    ///   - source: Filter by operation source (not yet supported)
    /// </summary>
    [McpForUnityTool("get_timeline", AutoRegister = false)]
    public static class GetTimelineTool
    {
        /// <summary>
        /// Main handler for timeline queries.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            // Delegate to the existing TimelineViewResource implementation
            return TimelineViewResource.HandleCommand(@params);
        }
    }
}
