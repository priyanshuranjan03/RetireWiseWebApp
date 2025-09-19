using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using RetireWiseWebApp.Models;
using RetireWiseWebApp.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace RetireWiseWebApp.Pages
{
    public class FileUploadModel : PageModel
    {
        private readonly PersistentAgentService _agentService;
        private readonly ILogger<FileUploadModel> _logger;

        public FileUploadModel(PersistentAgentService agentService, ILogger<FileUploadModel> logger)
        {
            _agentService = agentService;
            _logger = logger;
            Message = "Analyze my financial documents and provide retirement insights";
        }

        [BindProperty]
        [Display(Name = "Files")]
        public List<IFormFile> Files { get; set; } = new();

        [BindProperty]
        [Display(Name = "Message")]
        [Required(ErrorMessage = "Please provide a question or instruction for the analysis")]
        [StringLength(200, ErrorMessage = "Your message must be no longer than 200 characters")]
        public string Message { get; set; } = "Analyze my financial documents and provide retirement insights";

        [BindProperty]
        public string? ThreadId { get; set; }

        public bool IsConversationActive => _agentService.IsConversationActive;

        public FileUploadResult? Result { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public void OnGet()
        {
            // Initial page load
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var tempFilePaths = new List<string>();
            
            try
            {
                if (!ModelState.IsValid)
                {
                    StatusMessage = "Please correct the errors and try again.";
                    return Page();
                }

                if (Files == null || Files.Count == 0)
                {
                    StatusMessage = "Please select at least one file to upload.";
                    ModelState.AddModelError("Files", "Please select at least one file to upload.");
                    return Page();
                }

                // Validate file types and sizes
                foreach (var file in Files)
                {
                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var allowedExtensions = new[] { ".json", ".txt", ".csv", ".pdf", ".xlsx", ".docx", ".xls" };
                    
                    if (!allowedExtensions.Contains(extension))
                    {
                        StatusMessage = $"The file type {extension} is not supported.";
                        ModelState.AddModelError("Files", $"The file type {extension} is not supported.");
                        return Page();
                    }

                    // 10MB limit
                    if (file.Length > 10 * 1024 * 1024)
                    {
                        StatusMessage = "Files cannot be larger than 10MB.";
                        ModelState.AddModelError("Files", "Files cannot be larger than 10MB.");
                        return Page();
                    }
                }

                // Create temp directory
                var tempDir = Path.Combine(Path.GetTempPath(), "RetireWise_Uploads");
                Directory.CreateDirectory(tempDir);

                // Save uploaded files
                foreach (var formFile in Files)
                {
                    if (formFile.Length > 0)
                    {
                        // Create a safe filename
                        var safeFileName = Regex.Replace(Path.GetFileName(formFile.FileName), @"[^\w\.-]", "_");
                        var tempFilePath = Path.Combine(tempDir, safeFileName);
                        
                        using (var stream = new FileStream(tempFilePath, FileMode.Create))
                        {
                            await formFile.CopyToAsync(stream);
                        }
                        
                        tempFilePaths.Add(tempFilePath);
                        _logger.LogInformation($"Saved file {safeFileName} to temp directory");
                    }
                }

                if (!tempFilePaths.Any())
                {
                    StatusMessage = "No files were uploaded successfully.";
                    Result = new FileUploadResult
                    {
                        Success = false,
                        Message = "No files were uploaded successfully.",
                        FilesProcessed = 0,
                        AgentResponse = "",
                        Timestamp = DateTime.UtcNow
                    };
                    return Page();
                }

                // Call the agent service with uploaded files
                _logger.LogInformation($"Processing {tempFilePaths.Count} files with message: {Message}");
                var agentResult = await _agentService.RunAgentConversation(tempFilePaths, Message);
                
                // Clean up temp files
                foreach (var tempFile in tempFilePaths)
                {
                    if (System.IO.File.Exists(tempFile))
                    {
                        System.IO.File.Delete(tempFile);
                    }
                }

                // Create appropriate result using new formatting
                if (agentResult.Success)
                {
                    Result = FormatAgentResponse(agentResult.Response);
                    Result.FilesProcessed = tempFilePaths.Count;
                    Result.Message = "Files processed successfully";
                    
                    StatusMessage = "Your documents were analyzed successfully!";
                }
                else
                {
                    Result = new FileUploadResult
                    {
                        Success = false,
                        Message = agentResult.Response,
                        FilesProcessed = tempFilePaths.Count,
                        AgentResponse = "",
                        Messages = new List<AgentMessage> 
                        {
                            new AgentMessage 
                            { 
                                Type = MessageType.System,
                                Role = "System",
                                Content = $"Error: {agentResult.Response}"
                            }
                        },
                        Timestamp = DateTime.UtcNow
                    };
                    
                    StatusMessage = "There was an error analyzing your documents.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process files with agent");
                Result = new FileUploadResult
                {
                    Success = false,
                    Message = ex.Message,
                    FilesProcessed = tempFilePaths.Count,
                    AgentResponse = "",
                    Messages = new List<AgentMessage> 
                    {
                        new AgentMessage 
                        { 
                            Type = MessageType.System,
                            Role = "System",
                            Content = $"Error: {ex.Message}"
                        }
                    },
                    Timestamp = DateTime.UtcNow
                };
                
                StatusMessage = "An unexpected error occurred. Please try again.";
                
                // Clean up temp files in case of exception
                foreach (var tempFile in tempFilePaths)
                {
                    if (System.IO.File.Exists(tempFile))
                    {
                        System.IO.File.Delete(tempFile);
                    }
                }
            }
            
            return Page();
        }

        public async Task<IActionResult> OnPostContinueConversationAsync()
        {
            try
            {
                if (!_agentService.IsConversationActive)
                {
                    Result = new FileUploadResult
                    {
                        Success = false,
                        Message = "No active conversation. Please start a new one.",
                        Timestamp = DateTime.UtcNow,
                        Messages = new List<AgentMessage> 
                        {
                            new AgentMessage 
                            { 
                                Type = MessageType.System,
                                Role = "System",
                                Content = "No active conversation. Please start a new one."
                            }
                        }
                    };
                    return Page();
                }

                var agentResult = await _agentService.RunAgentConversation(
                    filePaths: null,
                    userMessage: Message,
                    isNewConversation: false
                );

                Result = FormatAgentResponse(agentResult.Response);
                Result.Success = agentResult.Success;
                Result.Message = agentResult.Success ? "Conversation continued successfully" : "Failed to continue conversation";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to continue conversation");
                Result = new FileUploadResult
                {
                    Success = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Messages = new List<AgentMessage> 
                    {
                        new AgentMessage 
                        { 
                            Type = MessageType.System,
                            Role = "System",
                            Content = $"Error: {ex.Message}"
                        }
                    }
                };
            }

            return Page();
        }

        public async Task<IActionResult> OnPostEndConversationAsync()
        {
            try
            {
                await _agentService.EndConversationAsync();
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to end conversation");
                Result = new FileUploadResult
                {
                    Success = false,
                    Message = "Failed to end conversation: " + ex.Message,
                    Messages = new List<AgentMessage> 
                    {
                        new AgentMessage 
                        { 
                            Type = MessageType.System,
                            Role = "System",
                            Content = $"Failed to end conversation: {ex.Message}"
                        }
                    },
                    Timestamp = DateTime.UtcNow
                };
                return Page();
            }
        }

        private FileUploadResult FormatAgentResponse(string rawResponse)
        {
            var result = new FileUploadResult
            {
                Success = true,
                Timestamp = DateTime.UtcNow,
                Messages = new List<AgentMessage>()
            };

            if (string.IsNullOrEmpty(rawResponse))
            {
                result.Messages.Add(new AgentMessage 
                { 
                    Type = MessageType.System,
                    Role = "System",
                    Content = "No response received from assistant."
                });
                return result;
            }

            // Debug: Log the raw response
            _logger.LogInformation($"Raw response length: {rawResponse.Length}");
            _logger.LogInformation($"Raw response content: {rawResponse}");

            // Extract all assistant message blocks
            var assistantBlocks = ExtractAllAssistantMessages(rawResponse);
            
            // Debug: Log parsed messages
            _logger.LogInformation($"Parsed {assistantBlocks.Count} assistant message blocks");
            for (int i = 0; i < assistantBlocks.Count; i++)
            {
                _logger.LogInformation($"Assistant message {i}: Length={assistantBlocks[i].Length}, Content={assistantBlocks[i]}");
            }

            // Assemble complete chat transcript for the UI
            result.Messages = new List<AgentMessage>
            {
                new AgentMessage
                {
                    Type = MessageType.User,
                    Role = "User",
                    Content = Message,
                    Timestamp = DateTime.UtcNow
                }
            };

            foreach (var ablock in assistantBlocks)
            {
                if (!string.IsNullOrWhiteSpace(ablock))
                {
                    result.Messages.Add(new AgentMessage
                    {
                        Type = MessageType.Assistant,
                        Role = "Assistant",
                        Content = ablock,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            return result;
        }

        private List<string> ExtractAllAssistantMessages(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse))
                return new List<string>();

            var lines = rawResponse.Replace("\r", "\n").Split('\n');
            List<string> messageBlocks = new();
            bool isAssistantBlock = false;
            var currentBlock = new List<string>();

            foreach (var line in lines)
            {
                if (line.Contains("Uploaded file") || line.Contains("Created vector store") || line.Contains("Created thread"))
                    continue;

                if (line.ToLower().Contains("- assistant:"))
                {
                    if (currentBlock.Count > 0)
                    {
                        messageBlocks.Add(string.Join("\n", currentBlock).Trim());
                        currentBlock.Clear();
                    }
                    isAssistantBlock = true;
                    var idx = line.IndexOf("- assistant:", StringComparison.InvariantCultureIgnoreCase);
                    var content = line.Substring(idx + 12).Trim();
                    
                    // Remove quotes if present
                    if (content.StartsWith("\"") && content.EndsWith("\""))
                    {
                        content = content.Substring(1, content.Length - 2);
                    }
                    
                    if (!string.IsNullOrWhiteSpace(content))
                        currentBlock.Add(content);
                    continue;
                }

                if (isAssistantBlock &&
                    (line.ToLower().Contains("- user:") || line.ToLower().Contains("- system:")))
                {
                    if (currentBlock.Count > 0)
                    {
                        messageBlocks.Add(string.Join("\n", currentBlock).Trim());
                        currentBlock.Clear();
                    }
                    isAssistantBlock = false;
                    continue;
                }

                if (isAssistantBlock)
                    currentBlock.Add(line);
            }
            
            if (currentBlock.Count > 0)
                messageBlocks.Add(string.Join("\n", currentBlock).Trim());

            return messageBlocks;
        }
    }

    public class FileUploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int FilesProcessed { get; set; }
        public string AgentResponse { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public List<AgentMessage> Messages { get; set; } = new();
    }
}
