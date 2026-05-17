using ChildDev.Api.Data;
using ChildDev.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Pages.Goals;

public class GoalsIndexModel(AppDbContext db) : PageModel
{
    public List<Goal> ActiveGoals { get; set; } = [];
    public List<Goal> CompletedGoals { get; set; } = [];
    [BindProperty] public string NewGoalText { get; set; } = string.Empty;
    [BindProperty] public string? NewMeasurableOutcome { get; set; }

    private string? GetAccountGuid() => HttpContext.Session.GetString("AccountGuid");

    public async Task<IActionResult> OnGetAsync()
    {
        var accountGuid = GetAccountGuid();
        if (accountGuid is null) return RedirectToPage("/Login");

        var goals = await db.Goals
            .Where(g => g.AccountFk == accountGuid && g.DeletedAt == null)
            .OrderByDescending(g => g.EnteredDate)
            .ToListAsync();

        ActiveGoals = goals.Where(g => g.CompletionDate == null).ToList();
        CompletedGoals = goals.Where(g => g.CompletionDate != null).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var accountGuid = GetAccountGuid();
        if (accountGuid is null) return RedirectToPage("/Login");

        var text = NewGoalText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return RedirectToPage("/Goals");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        db.Goals.Add(new Goal
        {
            Guid = Guid.NewGuid().ToString(),
            AccountFk = accountGuid,
            GoalText = text,
            MeasurableOutcome = string.IsNullOrWhiteSpace(NewMeasurableOutcome) ? null : NewMeasurableOutcome.Trim(),
            EnteredDate = now,
            UpdatedOn = now
        });
        await db.SaveChangesAsync();
        return RedirectToPage("/Goals/Index");
    }

    public async Task<IActionResult> OnPostCompleteAsync(string guid)
    {
        var accountGuid = GetAccountGuid();
        if (accountGuid is null) return RedirectToPage("/Login");

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Guid == guid && g.AccountFk == accountGuid);
        if (goal is not null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            goal.CompletionDate = now;
            goal.UpdatedOn = now;
            await db.SaveChangesAsync();
        }
        return RedirectToPage("/Goals/Index");
    }
}
