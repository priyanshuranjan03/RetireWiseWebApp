using Azure;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.VectorStores;
using RetireWiseWebApp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetireWiseWebApp.Services;

public class PersistentAgentService
{
    private readonly ILogger<PersistentAgentService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // --- Session state keys
    private const string SessionThreadId = "PA_ThreadId";
    private const string SessionIsActive = "PA_IsActive";
    private const string SessionFileIds = "PA_FileIds";
    private const string SessionVectorStoreIds = "PA_VectorStoreIds";

    // Cache for clients to reduce latency
    private PersistentAgentsClient? _cachedAgentsClient;
    private AIProjectClient? _cachedProjectClient;

    public PersistentAgentService(ILogger<PersistentAgentService> logger, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    // Helper: retrieve ISession
    private ISession Session => _httpContextAccessor.HttpContext!.Session;

    public string? CurrentThreadId
    {
        get => Session.GetString(SessionThreadId);
        private set => Session.SetString(SessionThreadId, value ?? "");
    }

    public bool IsConversationActive
    {
        get => Session.GetString(SessionIsActive) == "1";
        private set => Session.SetString(SessionIsActive, value ? "1" : "0");
    }

    private List<string> ActiveFileIds
    {
        get => Session.GetObject<List<string>>(SessionFileIds) ?? new List<string>();
        set => Session.SetObject(SessionFileIds, value);
    }

    private List<string> ActiveVectorStoreIds
    {
        get => Session.GetObject<List<string>>(SessionVectorStoreIds) ?? new List<string>();
        set => Session.SetObject(SessionVectorStoreIds, value);
    }

    // Optimized method to get clients (cached)
    private async Task<(AIProjectClient projectClient, PersistentAgentsClient agentsClient)> GetClientsAsync()
    {
        if (_cachedProjectClient == null || _cachedAgentsClient == null)
        {
            var endpointString = _configuration["PersistentAgent:Endpoint"];
            var endpoint = new Uri(endpointString);
            var credential = new AzureCliCredential();
            
            _cachedProjectClient = new AIProjectClient(endpoint, credential);
            _cachedAgentsClient = _cachedProjectClient.GetPersistentAgentsClient();
        }

        return (_cachedProjectClient, _cachedAgentsClient);
    }

    // Add method to end conversation and cleanup
    public async Task EndConversationAsync()
    {
        if (!IsConversationActive) return;
        try
        {
            var (projectClient, agentsClient) = await GetClientsAsync();
            var connectedAgentId = _configuration["PersistentAgent:ConnectedAgentId"];

            // Clean up vector stores
            foreach (var vectorStoreId in ActiveVectorStoreIds)
            {
                try
                {
                    await agentsClient.VectorStores.DeleteVectorStoreAsync(vectorStoreId);
                    _logger.LogInformation($"Deleted vector store: {vectorStoreId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete vector store {vectorStoreId}: {ex.Message}");
                }
            }

            // Clear vector stores from connected agent if it exists
            if (!string.IsNullOrEmpty(connectedAgentId))
            {
                try
                {
                    var agent = await agentsClient.Administration.GetAgentAsync(connectedAgentId);
                    if (agent.Value.ToolResources?.FileSearch?.VectorStores != null)
                    {
                        agent.Value.ToolResources.FileSearch.VectorStores.Clear();
                        await agentsClient.Administration.UpdateAgentAsync(
                            connectedAgentId,
                            tools: new List<ToolDefinition> { new FileSearchToolDefinition() },
                            toolResources: new ToolResources { FileSearch = new FileSearchToolResource() }
                        );
                        _logger.LogInformation($"Cleared vector stores from connected agent: {connectedAgentId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to clear vector stores from connected agent: {ex.Message}");
                }
            }

            // Delete uploaded files
            foreach (var fileId in ActiveFileIds)
            {
                try
                {
                    await agentsClient.Files.DeleteFileAsync(fileId);
                    _logger.LogInformation($"Deleted file: {fileId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete file {fileId}: {ex.Message}");
                }
            }

            // Delete the conversation thread
            if (!string.IsNullOrEmpty(CurrentThreadId))
            {
                try
                {
                    await agentsClient.Threads.DeleteThreadAsync(CurrentThreadId);
                    _logger.LogInformation($"Deleted thread: {CurrentThreadId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete thread {CurrentThreadId}: {ex.Message}");
                }
            }

            // Clear all tracked resources
            ActiveFileIds = new List<string>();
            ActiveVectorStoreIds = new List<string>();
            CurrentThreadId = null;
            IsConversationActive = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending conversation");
            throw;
        }
    }

    // RunAgentConversation manages state using session, never clearing unless EndConversationAsync is called
    public async Task<(bool Success, string Response)> RunAgentConversation(
        IEnumerable<string>? filePaths = null,
        string userMessage = "Hi Financial Agent!",
        bool isNewConversation = true)
    {
        try
        {
            if (isNewConversation)
            {
                if (IsConversationActive)
                    throw new InvalidOperationException("Please end the current conversation before starting a new one.");
            }
            else
            {
                if (!IsConversationActive || string.IsNullOrEmpty(CurrentThreadId))
                    throw new InvalidOperationException("No active conversation. Please start a new one.");
            }

            var mainAgentId = _configuration["PersistentAgent:MainAgentId"];
            var connectedAgentId = _configuration["PersistentAgent:ConnectedAgentId"];
            var responseBuilder = new StringBuilder();
            var fileIds = new List<string>();
            FileSearchToolResource? fileSearchRes = null;
            PersistentAgentThread? thread = null;
            
            // Use cached clients to reduce latency
            var (projectClient, agentsClient) = await GetClientsAsync();

            // Get the main agent first
            var mainAgentResponse = await agentsClient.Administration.GetAgentAsync(mainAgentId);
            var mainAgent = mainAgentResponse.Value;
            _logger.LogInformation($"Retrieved main agent: {mainAgentId}");

            if (isNewConversation)
            {
                IsConversationActive = true;
                // Only upload files and create vector store for new conversations
                if (filePaths != null && filePaths.Any())
                {
                    foreach (var filePath in filePaths)
                    {
                        if (File.Exists(filePath))
                        {
                            var uploadResponse = await agentsClient.Files.UploadFileAsync(filePath: filePath, purpose: PersistentAgentFilePurpose.Agents);
                            var uploadedFile = uploadResponse.Value;
                            fileIds.Add(uploadedFile.Id);
                            TrackResource(fileId: uploadedFile.Id); // Store for session
                            var fileUploadMessage = $"Uploaded file, ID: {uploadedFile.Id}, name: {uploadedFile.Filename}";
                            _logger.LogInformation(fileUploadMessage);
                            responseBuilder.AppendLine(fileUploadMessage);
                        }
                        else
                        {
                            _logger.LogWarning($"File not found: {filePath}");
                        }
                    }
                }
                if (!fileIds.Any())
                    throw new InvalidOperationException("No valid files were uploaded. Please provide valid file paths.");

                var vectorStoreResponse = await agentsClient.VectorStores.CreateVectorStoreAsync(fileIds: fileIds, name: $"VectorStore_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
                var vectorStore = vectorStoreResponse.Value;
                TrackResource(vectorStoreId: vectorStore.Id);
                var vectorStoreMessage = $"Created vector store, ID: {vectorStore.Id}";
                _logger.LogInformation(vectorStoreMessage);
                responseBuilder.AppendLine(vectorStoreMessage);

                fileSearchRes = new FileSearchToolResource();
                fileSearchRes.VectorStoreIds.Add(vectorStore.Id);

                await agentsClient.Administration.UpdateAgentAsync(
                    connectedAgentId,
                    tools: new List<ToolDefinition> { new FileSearchToolDefinition() },
                    toolResources: new ToolResources { FileSearch = fileSearchRes }
                );

                var threadResponse = await agentsClient.Threads.CreateThreadAsync();
                thread = threadResponse.Value;
                CurrentThreadId = thread.Id;
                var threadCreatedMessage = $"Created thread, ID: {CurrentThreadId}";
                _logger.LogInformation(threadCreatedMessage);
                responseBuilder.AppendLine(threadCreatedMessage);
            }
            else
            {
                try
                {
                    var threadResponse = await agentsClient.Threads.GetThreadAsync(CurrentThreadId);
                    thread = threadResponse.Value;
                    _logger.LogInformation($"Retrieved existing thread: {CurrentThreadId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to retrieve thread {CurrentThreadId}");
                    IsConversationActive = false;
                    CurrentThreadId = null;
                    throw new InvalidOperationException("Conversation session expired. Please start a new conversation.");
                }
            }
            var messageResponse = await agentsClient.Messages.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                userMessage);
            var runResponse = await agentsClient.Runs.CreateRunAsync(
                thread.Id,
                mainAgent.Id);
            var run = runResponse.Value;
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                var runStatusResponse = await agentsClient.Runs.GetRunAsync(thread.Id, run.Id);
                run = runStatusResponse.Value;
            }
            while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

            if (run.Status != RunStatus.Completed)
                throw new InvalidOperationException($"Run failed or was canceled: {run.LastError?.Message}");

            var messages = agentsClient.Messages.GetMessages(
                thread.Id,
                order: ListSortOrder.Descending,
                limit: 2
            );
            foreach (var message in messages.Reverse())
            {
                var formattedMessage = $"{message.CreatedAt:yyyy-MM-dd HH:mm:ss} - {message.Role}: ";
                var sb = new StringBuilder();
                foreach (var contentItem in message.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        sb.Append(textItem.Text);
                    }
                    else if (contentItem is MessageImageFileContent imageFileItem)
                    {
                        sb.Append($"<image from ID: {imageFileItem.FileId}>");
                    }
                }
                formattedMessage += sb.ToString();
                responseBuilder.AppendLine(formattedMessage);
                _logger.LogInformation(formattedMessage);
            }
            return (true, responseBuilder.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent conversation");
            if (isNewConversation)
            {
                await CleanupFailedConversation();
            }
            return (false, ex.Message);
        }
    }

    private async Task CleanupFailedConversation()
    {
        IsConversationActive = false;
        CurrentThreadId = null;
        ActiveFileIds = new List<string>();
        ActiveVectorStoreIds = new List<string>();
    }

    // Use session object, not instance fields!
    private void TrackResource(string? fileId = null, string? vectorStoreId = null)
    {
        if (fileId != null)
        {
            var files = ActiveFileIds;
            files.Add(fileId);
            ActiveFileIds = files;
        }
        if (vectorStoreId != null)
        {
            var vs = ActiveVectorStoreIds;
            vs.Add(vectorStoreId);
            ActiveVectorStoreIds = vs;
        }
    }
}
