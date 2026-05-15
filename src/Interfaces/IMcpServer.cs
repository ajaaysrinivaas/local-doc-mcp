using System.Threading.Tasks;

namespace DocumentRagMcpServer.Interfaces
{
    /// <summary>
    /// MCP server that handles stdio communication.
    /// Manages the protocol loop and message processing.
    /// </summary>
    public interface IMcpServer
    {
        /// <summary>Start the MCP server and begin reading from stdin.</summary>
        Task RunAsync();
    }
}
