using Azure;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Core;
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
    private PersistentAgent? _cachedMainAgent;
    private string? _cachedMainAgentId;

    public PersistentAgentService(ILogger<PersistentAgentService> logger, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

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
            var endpointString = _configuration["PersistentAgent:Endpoint"] ?? _configuration["PersistentAgent_Endpoint"];
            if (string.IsNullOrEmpty(endpointString))
                throw new InvalidOperationException("PersistentAgent endpoint configuration is missing");

            var endpoint = new Uri(endpointString);

            // Fast credential selection: ManagedIdentity in Azure, CLI locally
            TokenCredential credential;
            var websiteName = _configuration["WEBSITE_SITE_NAME"] ?? Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            if (!string.IsNullOrEmpty(websiteName))
            {
                credential = new ManagedIdentityCredential();
                _logger.LogInformation("Using ManagedIdentityCredential for Azure App Service: {WebsiteName}", websiteName);
            }
            else
            {
                credential = new AzureCliCredential();
                _logger.LogInformation("Using AzureCliCredential for local development");
            }
            _cachedProjectClient = new AIProjectClient(endpoint, credential);
            _cachedAgentsClient = _cachedProjectClient.GetPersistentAgentsClient();
        }
        return (_cachedProjectClient, _cachedAgentsClient);
    }
    public async Task EndConversationAsync()
    {
        if (!IsConversationActive) return;
        try
        {
            var (projectClient, agentsClient) = await GetClientsAsync();
            var connectedAgentId = _configuration["PersistentAgent:ConnectedAgentId"] ?? _configuration["PersistentAgent_ConnectedAgentId"];
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
            if (!string.IsNullOrEmpty(connectedAgentId))
            {
                try
                {
                    var agent = await agentsClient.Administration.GetAgentAsync(connectedAgentId);
                    if (agent.Value.ToolResources?.FileSearch?.VectorStores != null)
                    {
                        agent.Value.ToolResources.FileSearch.VectorStores.Clear();
                        var updateAgentTask = agentsClient.Administration.UpdateAgentAsync(
                            connectedAgentId,
                            tools: new List<ToolDefinition> { new FileSearchToolDefinition() },
                            toolResources: new ToolResources { FileSearch = new FileSearchToolResource() }
                        );
                        _ = updateAgentTask.ContinueWith(t =>
                        {
                            if (t.IsCompletedSuccessfully)
                                _logger.LogInformation("Agent updated successfully");
                            else
                                _logger.LogWarning("Failed to update agent");
                        });
                        _logger.LogInformation($"Cleared vector stores from connected agent: {connectedAgentId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to clear vector stores from connected agent: {ex.Message}");
                }
            }
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

    public async Task<(bool Success, string Response)> RunAgentConversation(
        IEnumerable<string>? filePaths = null,
        string userMessage = "Hi Financial Agent!",
        bool isNewConversation = true)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("=== Starting conversation (New: {IsNew}) ===", isNewConversation);

            if (isNewConversation && IsConversationActive)
                throw new InvalidOperationException("Please end the current conversation before starting a new one.");

            if (!isNewConversation && (!IsConversationActive || string.IsNullOrEmpty(CurrentThreadId)))
                throw new InvalidOperationException("No active conversation. Please start a new one.");

            var mainAgentId = _configuration["PersistentAgent:MainAgentId"] ?? _configuration["PersistentAgent_MainAgentId"];
            var connectedAgentId = _configuration["PersistentAgent:ConnectedAgentId"] ?? _configuration["PersistentAgent_ConnectedAgentId"];
            var responseBuilder = new StringBuilder();
            FileSearchToolResource? fileSearchRes = null;
            PersistentAgentThread? thread = null;

            // ========== CLIENT/AGENT RETRIEVAL ==========
            stepStopwatch.Restart();
            var (projectClient, agentsClient) = await GetClientsAsync();
            _logger.LogInformation("✓ Clients retrieved in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);

            stepStopwatch.Restart();
            PersistentAgent mainAgent;
            if (_cachedMainAgent != null && _cachedMainAgentId == mainAgentId)
            {
                mainAgent = _cachedMainAgent;
                _logger.LogInformation("✓ Main agent from cache in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);
            }
            else
            {
                var mainAgentResponse = await agentsClient.Administration.GetAgentAsync(mainAgentId);
                mainAgent = mainAgentResponse.Value;
                _cachedMainAgent = mainAgent;
                _cachedMainAgentId = mainAgentId;
                _logger.LogInformation("✓ Main agent from service in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);
            }

            // ========== FILES/VECTOR STORE/THREAD CREATION ==========
            if (isNewConversation)
            {
                IsConversationActive = true;
                var fileIds = new List<string>();
                if (filePaths != null && filePaths.Any())
                {
                    var validFilePaths = filePaths.Where(File.Exists).ToList();
                    _logger.LogInformation("Uploading {FileCount} files...", validFilePaths.Count);

                    // --- Async Upload All Files ---
                    var uploadTasks = validFilePaths.Select(async filePath =>
                    {
                        try
                        {
                            var uploadResponse = await agentsClient.Files.UploadFileAsync(
                                filePath: filePath,
                                purpose: PersistentAgentFilePurpose.Agents);
                            var uploadedFile = uploadResponse.Value;
                            _logger.LogInformation("✓ Uploaded file: {0}", uploadedFile.Id);
                            return new { Success = true, FileId = uploadedFile.Id };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed upload: {0}", filePath);
                            return new { Success = false, FileId = "" };
                        }
                    });
                    var uploadResults = await Task.WhenAll(uploadTasks);
                    foreach (var result in uploadResults)
                        if (result.Success) fileIds.Add(result.FileId);
                }

                if (!fileIds.Any())
                    throw new InvalidOperationException("No valid files were uploaded. Please provide valid file paths.");

                // === Parallel vector store + thread creation ===
                var vectorStoreTask = agentsClient.VectorStores.CreateVectorStoreAsync(
                    fileIds: fileIds,
                    name: $"VectorStore_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                );
                var threadTask = agentsClient.Threads.CreateThreadAsync();
                await Task.WhenAll(vectorStoreTask, threadTask);

                var vectorStore = vectorStoreTask.Result.Value;
                thread = threadTask.Result.Value;
                TrackResource(vectorStoreId: vectorStore.Id);
                CurrentThreadId = thread.Id;

                // ---- Fire & forget agent update (non-blocking) ----
                fileSearchRes = new FileSearchToolResource();
                fileSearchRes.VectorStoreIds.Add(vectorStore.Id);
                var updateAgentTask = agentsClient.Administration.UpdateAgentAsync(
                    connectedAgentId,
                    tools: new List<ToolDefinition> { new FileSearchToolDefinition() },
                    toolResources: new ToolResources { FileSearch = fileSearchRes }
                );
                _ = updateAgentTask.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        _logger.LogInformation("Agent updated successfully");
                    else if (t.Exception != null)
                        _logger.LogWarning(t.Exception, "Failed to update agent");
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                try
                {
                    var threadResponse = await agentsClient.Threads.GetThreadAsync(CurrentThreadId);
                    thread = threadResponse.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve thread {0}", CurrentThreadId);
                    IsConversationActive = false;
                    CurrentThreadId = null;
                    throw new InvalidOperationException("Conversation session expired. Please start a new conversation.");
                }
            }

            // ========== SEND/WAIT FOR AGENT ==========
            // Send message (always async)
            var messageResponse = await agentsClient.Messages.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                userMessage);

            // Start run
            var runResponse = await agentsClient.Runs.CreateRunAsync(thread.Id, mainAgent.Id);
            var run = runResponse.Value;

            // --- OPTIMIZED POLLING ---
            var pollDelay = isNewConversation ? 75 : 30;           
            var maxDelay = isNewConversation ? 350 : 150;         
            var pollCount = 0;
            var lastStatus = run.Status;
            var maxPollTime = isNewConversation ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(18); // Reduced max per turn

            var pollingSw = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(pollDelay));
                var runStatusResponse = await agentsClient.Runs.GetRunAsync(thread.Id, run.Id);
                run = runStatusResponse.Value;

                pollCount++;
                if (run.Status != lastStatus)
                {
                    _logger.LogInformation("Run status changed: {OldStatus} → {NewStatus} (poll #{PollCount})",
                        lastStatus, run.Status, pollCount);
                    lastStatus = run.Status;
                }
                // Faster ramp to max polling intervals
                if (pollCount > (isNewConversation ? 2 : 1))
                {
                    pollDelay = Math.Min(pollDelay + (isNewConversation ? 50 : 20), maxDelay);
                }
                if (pollingSw.Elapsed > maxPollTime)
                {
                    _logger.LogError("Run polling timeout after {ElapsedMs}ms", pollingSw.ElapsedMilliseconds);
                    throw new TimeoutException($"AI processing took too long (>{maxPollTime.TotalSeconds}s). Please try again.");
                }
                if (pollCount % 5 == 0)
                {
                    _logger.LogWarning("Long-running operation: {Status} for {ElapsedMs}ms (poll #{PollCount})",
                        run.Status, pollingSw.ElapsedMilliseconds, pollCount);
                }
            } while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

            if (run.Status != RunStatus.Completed)
                throw new InvalidOperationException($"Run failed or was canceled: {run.LastError?.Message}");

            // === RESPONSE HANDLING ===
            var messages = agentsClient.Messages.GetMessages(
                thread.Id,
                order: ListSortOrder.Descending,
                limit: 1
            );

            var latestMessage = messages.FirstOrDefault();
            if (latestMessage != null && latestMessage.Role != MessageRole.User)
            {
                var formattedMessage = $"{latestMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {latestMessage.Role}: ";
                var sb = new StringBuilder();
                foreach (var contentItem in latestMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                        sb.Append(textItem.Text);
                    else if (contentItem is MessageImageFileContent imageFileItem)
                        sb.Append($"<image from ID: {imageFileItem.FileId}>");
                }
                formattedMessage += sb.ToString();
                responseBuilder.AppendLine(formattedMessage);
            }
            else
            {
                _logger.LogWarning("No assistant response found in messages");
                responseBuilder.AppendLine("No response received from assistant.");
            }

            _logger.LogInformation("=== TOTAL CONVERSATION TIME: {ElapsedMs}ms ===", stopwatch.ElapsedMilliseconds);
            return (true, responseBuilder.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Error during agent conversation after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            if (isNewConversation)
                await CleanupFailedConversation();
            return (false, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private Task CleanupFailedConversation()
    {
        IsConversationActive = false;
        CurrentThreadId = null;
        ActiveFileIds = new List<string>();
        ActiveVectorStoreIds = new List<string>();
        return Task.CompletedTask;
    }
    private void TrackResource(String? fileId = null, String? vectorStoreId = null)
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
    public async Task<bool> PreWarmConnectionAsync()
    {
        try
        {
            var (projectClient, agentsClient) = await GetClientsAsync();
            return true;
        }
        catch { return false; }
    }
}
