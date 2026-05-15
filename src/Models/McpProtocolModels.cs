using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocumentRagMcpServer.Models
{
    // MCP Protocol Messages
    public class McpMessage
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Id { get; set; }

        [JsonPropertyName("method")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Method { get; set; }

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Params { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ErrorObject? Error { get; set; }
    }

    public class ErrorObject
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    public class Tool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("inputSchema")]
        public object InputSchema { get; set; } = new();
    }

    public class ListToolsResult
    {
        [JsonPropertyName("tools")]
        public List<Tool> Tools { get; set; } = new();
    }

    public class CallToolParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("arguments")]
        public Dictionary<string, object>? Arguments { get; set; }
    }

    public class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonPropertyName("capabilities")]
        public Dictionary<string, object> Capabilities { get; set; } = new();

        [JsonPropertyName("serverInfo")]
        public Dictionary<string, string> ServerInfo { get; set; } = new();
    }
}
