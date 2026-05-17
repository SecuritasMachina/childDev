using BCrypt.Net;
using ChildDev.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Pages;

public class LoginModel(AppDbContext db) : PageModel
{
    [BindProperty] public string NickName { get; set; } = string.Empty;
    [BindProperty] public string Pin { get; set; } = string.Empty;
    public string? Error { get; set; }

    public IActionResult OnGet() =>
        HttpContext.Session.GetString("AccountGuid") is not null
            ? RedirectToPage("/Index")
            : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var nick = NickName.Trim();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.NickName == nick);
        var pinHash = BCrypt.Net.BCrypt.HashPassword(Pin);
        if (account is null || !BCrypt.Net.BCrypt.Verify(pinHash, account.PinHash))
        {
            Error = "Invalid nickname or PIN.";
            return Page();
        }

        HttpContext.Session.SetString("AccountGuid", account.Guid);
        HttpContext.Session.SetString("NickName", account.NickName);
        return RedirectToPage("/Index");
    }
}
