using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public string? AccountGuid { get; set; }
    public List<Goal> Goals { get; set; } = [];
    [BindProperty] public string NewGoalText { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AccountGuid = HttpContext.Session.GetString("AccountGuid");
        if (AccountGuid is null) return;
        Goals = await db.Goals
            .Where(g => g.AccountFk == AccountGuid && g.DeletedAt == null && g.CompletionDate == null)
            .OrderByDescending(g => g.EnteredDate)
            .Take(5)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAddGoalAsync()
    {
        var accountGuid = HttpContext.Session.GetString("AccountGuid");
        if (accountGuid is null) return RedirectToPage("/Login");

        var text = NewGoalText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return RedirectToPage("/Index");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        db.Goals.Add(new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = accountGuid,
            GoalText = text,
            EnteredDate = now,
            UpdatedOn = now
        });
        await db.SaveChangesAsync();
        return RedirectToPage("/Index");
    }
}
