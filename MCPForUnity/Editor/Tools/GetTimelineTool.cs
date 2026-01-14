using System;
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
    /// </summary>
    [McpForUnityTool("get_timeline", AutoRegister = false)]
    public static class GetTimelineTool
    {
        /// <summary>
        /// Parameters for get_timeline tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Maximum number of events to return (1-1000, default: 50)
            /// </summary>
            [ToolParameter("Maximum number of events to return (1-1000, default: 50)", Required = false, DefaultValue = "50")]
            public int Limit { get; set; } = 50;

            /// <summary>
            /// Only return events after this sequence number
            /// </summary>
            [ToolParameter("Only return events after this sequence number", Required = false)]
            public long SinceSequence { get; set; }

            /// <summary>
            /// Include context associations (default: false)
            /// </summary>
            [ToolParameter("Include context associations (default: false)", Required = false, DefaultValue = "false")]
            public bool IncludeContext { get; set; } = false;

            /// <summary>
            /// Include importance, category, intent (default: false)
            /// </summary>
            [ToolParameter("Include importance, category, intent (default: false)", Required = false, DefaultValue = "false")]
            public bool IncludeSemantics { get; set; } = false;

            /// <summary>
            /// Filter by operation source (not yet supported)
            /// </summary>
            [ToolParameter("Filter by operation source (not yet supported)", Required = false)]
            public string Source { get; set; }
        }

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
