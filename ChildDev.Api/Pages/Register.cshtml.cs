using BCrypt.Net;
using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Pages;

public class RegisterModel(AppDbContext db) : PageModel
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
        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 50)
        {
            Error = "Nickname must be 1–50 characters.";
            return Page();
        }
        if (await db.Accounts.AnyAsync(a => a.NickName == nick))
        {
            Error = "Nickname already taken.";
            return Page();
        }

        var pinHash = BCrypt.Net.BCrypt.HashPassword(Pin);
        var account = new Account
        {
            Guid = Guid.NewGuid().ToString(),
            NickName = nick,
            PinHash = BCrypt.Net.BCrypt.HashPassword(pinHash),
            CreatedOn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        HttpContext.Session.SetString("AccountGuid", account.Guid);
        HttpContext.Session.SetString("NickName", account.NickName);
        return RedirectToPage("/Index");
    }
}
