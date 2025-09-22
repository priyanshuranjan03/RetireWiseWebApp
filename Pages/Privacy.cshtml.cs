using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RetireWiseWebApp.Pages
{
    public class PrivacyModel : PageModel
    {
        private readonly ILogger<PrivacyModel> _logger;

        public PrivacyModel(ILogger<PrivacyModel> logger)
        {
            _logger = logger;
        }

        public DateTime LastUpdated { get; private set; }

        public void OnGet()
        {
            LastUpdated = DateTime.Now;
            _logger.LogInformation("Privacy Policy page accessed at {Timestamp}", DateTime.UtcNow);
        }

        public IActionResult OnPostContact()
        {
            // This could be used for a contact form in the future
            return RedirectToPage();
        }
    }
}
