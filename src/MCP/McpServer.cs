using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.MCP
{
    /// <summary>
    /// MCP server that listens on stdin and writes to stdout.
    /// Handles the protocol loop and message dispatch.
    /// </summary>
    public class McpServer : IMcpServer
    {
        private readonly IMcpMessageHandler _messageHandler;
        private readonly JsonSerializerOptions _jsonOptions;

        public McpServer(IMcpMessageHandler messageHandler)
        {
            _messageHandler = messageHandler;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                // Use default encoder that properly escapes all control characters
                // UnsafeRelaxedJsonEscaping was causing \u0007 and other control chars to leak into JSON
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default
            };
        }

        public async Task RunAsync()
        {
            var input = Console.In;

            while (true)
            {
                string? line;
                try
                {
                    line = await input.ReadLineAsync();
                }
                catch
                {
                    break;
                }

                if (line == null) break; // EOF
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var message = JsonSerializer.Deserialize<McpMessage>(line, _jsonOptions);
                    if (message == null)
                        continue;

                    var response = await _messageHandler.HandleMessageAsync(message);
                    WriteResponse(response);
                }
                catch (Exception ex)
                {
                    var errorResponse = new McpMessage
                    {
                        Error = new ErrorObject { Code = -32603, Message = ex.Message }
                    };
                    WriteResponse(errorResponse);
                }
            }
        }

        private void WriteResponse(McpMessage response)
        {
            var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
            Console.WriteLine(responseJson);
            Console.Out.Flush();
        }
    }
}
