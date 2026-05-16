using System.Collections.ObjectModel;
using ChildDev.Mobile.Data;
using ChildDev.Mobile.Models;
using ChildDev.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildDev.Mobile.ViewModels;

public partial class JournalListViewModel(
    JournalRepository repo,
    AccountService accountService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Journal> journals = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var account = await accountService.GetAccountAsync();
        if (account is null) return;
        var items = await repo.GetAllActiveAsync(account.Guid);
        Journals = new ObservableCollection<Journal>(items);
    }

    [RelayCommand]
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("journal/entry");

    [RelayCommand]
    private async Task OpenAsync(Journal journal) =>
        await Shell.Current.GoToAsync($"journal/entry?guid={journal.Guid}");

    [RelayCommand]
    private async Task DeleteAsync(Journal journal)
    {
        await repo.DeleteAsync(journal.Guid);
        Journals.Remove(journal);
    }
}
