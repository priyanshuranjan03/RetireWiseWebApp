# RetireWise - AI-Powered Financial Analysis Web Application

RetireWise is an intelligent financial planning web application that uses Azure AI Agents to analyze financial documents and provide personalized retirement insights. Built with ASP.NET Core Razor Pages and integrated with Azure AI services.

## ?? Features

- **AI-Powered Document Analysis**: Upload financial documents (PDF, Excel, CSV) for intelligent analysis
- **Interactive Chat Interface**: Conversational AI for personalized financial advice
- **Retirement Planning**: Get tailored retirement savings and investment recommendations
- **Secure Document Processing**: Encrypted file upload and processing with automatic cleanup
- **Markdown-Formatted Responses**: Rich text formatting for better readability
- **Responsive Design**: Modern UI that works on desktop and mobile devices

## ??? Architecture

- **Frontend**: ASP.NET Core Razor Pages with Bootstrap 5
- **Backend**: .NET 8 with Azure AI Agents integration
- **AI Services**: Azure AI Project with Persistent Agents
- **Document Processing**: Azure AI File Search with Vector Store
- **Session Management**: Server-side session state for conversation continuity

## ?? Prerequisites

Before you begin, ensure you have the following installed:

- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Visual Studio 2022** or **Visual Studio Code** with C# extension
- **Azure CLI** ([Download](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli))
- **Azure Subscription** with AI Services enabled
- **Git** for version control

## ?? Azure Setup

### 1. Create Azure AI Project

1. Go to [Azure Portal](https://portal.azure.com)
2. Create a new **Azure AI Studio** project
3. Note down the following values:
   - Project Endpoint URL
   - Main Agent ID
   - Connected Agent ID

### 2. Configure Azure CLI Authentication

```bash
# Login to Azure
az login

# Set your subscription (optional)
az account set --subscription "your-subscription-id"
```

## ?? Installation & Setup

### 1. Clone the Repository

```bash
git clone https://github.com/priyanshuranjan03/RetireWiseWebApp.git
cd RetireWiseWebApp
```

### 2. Configure Application Settings

Create or update `appsettings.json` with your Azure AI configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PersistentAgent": {
    "Endpoint": "https://your-project-name.services.ai.azure.com/api/projects/your-project-id",
    "MainAgentId": "asst_xxxxxxxxxxxxxxxxxxxxx",
    "ConnectedAgentId": "asst_xxxxxxxxxxxxxxxxxxxxx"
  }
}
```

**Note**: Replace the placeholder values with your actual Azure AI project details.

### 3. Install Dependencies

```bash
# Restore NuGet packages
dotnet restore
```

### 4. Build the Project

```bash
# Build the solution
dotnet build
```

## ?? Running the Application

### Development Mode

```bash
# Run the application in development mode
dotnet run
```

Or press `F5` in Visual Studio to start debugging.

The application will be available at:
- **HTTPS**: `https://localhost:7001`
- **HTTP**: `http://localhost:5000`

### Production Mode

```bash
# Build for production
dotnet publish -c Release -o ./publish

# Run the published application
cd publish
dotnet RetireWiseWebApp.dll
```

## ?? Usage Guide

### 1. Access the Application

1. Open your web browser
2. Navigate to `https://localhost:7001`
3. You'll see the RetireWise homepage

### 2. Upload Financial Documents

1. Click **"Get Started"** or navigate to **"Document Analysis"**
2. Upload your financial documents:
   - **Supported formats**: PDF, Excel (.xlsx, .xls), CSV, TXT, JSON, Word (.docx)
   - **File size limit**: 10MB per file
   - **Multiple files**: You can upload several documents at once

3. Enter your financial question (e.g., "How can I improve my retirement savings?")
4. Click **"Analyze Documents"**

### 3. Interactive Chat

1. After document analysis, you'll enter an interactive chat mode
2. Ask follow-up questions about your financial situation
3. Get personalized investment and retirement advice
4. All responses are formatted with markdown for better readability

### 4. End Session

1. Click **"End Chat"** when you're done
2. Confirm the action in the dialog
3. All uploaded files and conversation data will be securely deleted

## ?? Configuration Options

### Session Settings

You can configure session behavior in `appsettings.json`:

```json
{
  "Session": {
    "TimeoutMinutes": 30,
    "CleanupOnClose": true,
    "AutoCleanupEnabled": true
  }
}
```

### File Upload Limits

File upload constraints are defined in `FileUpload.cshtml.cs`:

```csharp
// Supported file extensions
var allowedExtensions = new[] { ".json", ".txt", ".csv", ".pdf", ".xlsx", ".docx", ".xls" };

// File size limit (10MB)
if (file.Length > 10 * 1024 * 1024) // 10MB limit
```

## ??? Project Structure

```
RetireWiseWebApp/
??? Controllers/           # API controllers (if any)
??? Extensions/           # Extension methods
??? Helpers/             # Utility classes
?   ??? MarkdownHelper.cs   # Markdown to HTML conversion
??? Middleware/          # Custom middleware
??? Models/              # Data models
?   ??? AgentMessage.cs     # Chat message model
?   ??? AgentModels.cs      # Agent-related models
??? Pages/               # Razor Pages
?   ??? FileUpload.cshtml   # Main document analysis page
?   ??? Index.cshtml        # Homepage
?   ??? Privacy.cshtml      # Privacy policy
?   ??? Shared/            # Shared layouts and partials
??? Services/            # Business logic services
?   ??? PersistentAgentService.cs  # Azure AI integration
??? wwwroot/            # Static files
?   ??? css/               # Stylesheets
?   ??? js/                # JavaScript files
?   ??? lib/               # Third-party libraries
??? appsettings.json    # Application configuration
??? Program.cs          # Application entry point
??? RetireWiseWebApp.csproj  # Project file
```

## ?? Security Features

- **Encrypted File Processing**: All uploaded files are processed securely
- **Automatic Cleanup**: Files and conversation data are automatically deleted
- **Session Management**: Secure server-side session handling
- **Azure Authentication**: Uses Azure CLI credentials for service authentication

## ?? Troubleshooting

### Common Issues

1. **Azure Authentication Errors**
   ```bash
   # Re-login to Azure CLI
   az logout
   az login
   ```

2. **File Upload Failures**
   - Check file size (must be < 10MB)
   - Verify file format is supported
   - Ensure stable internet connection

3. **Agent Response Issues**
   - Verify Azure AI project configuration
   - Check agent IDs in `appsettings.json`
   - Review application logs for detailed error messages

### Debug Mode

Run the application in debug mode to see detailed logs:

```bash
dotnet run --environment Development
```

Check the console output for detailed error messages and Azure service responses.

## ?? Dependencies

### Main Packages

- **Microsoft.AspNetCore.App** (8.0) - ASP.NET Core framework
- **Azure.AI.Agents.Persistent** - Azure AI Agents integration
- **Azure.AI.Projects** - Azure AI Projects client
- **Azure.Identity** - Azure authentication
- **Markdig** - Markdown processing
- **Newtonsoft.Json** - JSON serialization
- **Bootstrap** (5.x) - UI framework
- **Bootstrap Icons** - Icon library

### Development Packages

- **Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation** - Razor runtime compilation
- **Microsoft.VisualStudio.Web.CodeGeneration.Design** - Code generation tools

## ?? Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## ?? License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ????? Author

**Priyanshu Ranjan**
- GitHub: [@priyanshuranjan03](https://github.com/priyanshuranjan03)
- Project: [RetireWiseWebApp](https://github.com/priyanshuranjan03/RetireWiseWebApp)

## ?? Acknowledgments

- **Azure AI Services** for providing the AI capabilities
- **Bootstrap** for the responsive UI framework
- **Markdig** for markdown processing
- **.NET Community** for the excellent documentation and support

---

## ?? Support

If you encounter any issues or have questions:

1. Check the [Issues](https://github.com/priyanshuranjan03/RetireWiseWebApp/issues) page
2. Create a new issue with detailed information
3. Include error messages, logs, and steps to reproduce

**Happy Financial Planning! ????**