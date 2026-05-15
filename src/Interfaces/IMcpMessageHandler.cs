using System.Threading.Tasks;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Interfaces
{
    /// <summary>
    /// Handler for MCP protocol messages.
    /// Processes requests and generates responses.
    /// </summary>
    public interface IMcpMessageHandler
    {
        /// <summary>Handle an incoming MCP message.</summary>
        McpMessage HandleMessage(McpMessage message);

        /// <summary>Load and initialise the document index.</summary>
        Task InitializeAsync();
    }
}
