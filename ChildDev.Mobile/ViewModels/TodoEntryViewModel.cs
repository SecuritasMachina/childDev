using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

[QueryProperty(nameof(Guid), "guid")]
public partial class TodoEntryViewModel(
    TodoRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty] private string guid = string.Empty;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private int titleLength;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private int notesLength;
    [ObservableProperty] private bool hasDueDate;
    [ObservableProperty] private DateTime dueDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private bool isExisting;

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    partial void OnTitleChanged(string value)
    {
        TitleLength = value?.Length ?? 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnNotesChanged(string value) => NotesLength = value?.Length ?? 0;

    partial void OnGuidChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            LoadAsync(value).FireAndForget();
    }

    private async Task LoadAsync(string guid)
    {
        var item = await repo.GetAsync(guid);
        if (item is null) return;
        Title = item.Title ?? string.Empty;
        Notes = item.Notes ?? string.Empty;
        if (item.DueDate.HasValue)
        {
            DueDate = DateTimeOffset.FromUnixTimeMilliseconds(item.DueDate.Value).LocalDateTime;
            HasDueDate = true;
        }
        IsExisting = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = string.IsNullOrEmpty(Guid)
            ? new Todo { Guid = System.Guid.NewGuid().ToString(), AccountFk = account.Guid }
            : await repo.GetAsync(Guid) ?? new Todo { Guid = Guid, AccountFk = account.Guid };

        todo.Title = Title.Trim();
        todo.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        todo.DueDate = HasDueDate
            ? new DateTimeOffset(DateTime.SpecifyKind(DueDate, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : null;

        await repo.SaveAsync(todo);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task MarkDoneAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.CompleteAsync(Guid);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        await repo.DeleteAsync(Guid);
        await Shell.Current.GoToAsync("..");
    }
}
