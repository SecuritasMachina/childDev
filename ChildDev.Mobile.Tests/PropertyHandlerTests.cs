using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

/// <summary>
/// Covers OnEmotionReasonChanged and TodoListViewModel AddAsync lazy _accountGuid init.
/// </summary>
public class JournalEntryEmotionReasonTests : ViewModelTestBase
{
    private JournalEntryViewModel BuildVm() =>
        new(JournalRepo, AccountService, Analytics, Nav, ReminderSvc);

    [Fact]
    public void OnEmotionReasonChanged_UpdatesEmotionReasonLength()
    {
        var vm = BuildVm();
        vm.EmotionReason = "Feeling motivated today";
        Assert.Equal("Feeling motivated today".Length, vm.EmotionReasonLength);
    }

    [Fact]
    public void OnEmotionReasonChanged_Null_SetsLengthZero()
    {
        var vm = BuildVm();
        vm.EmotionReason = "Some reason";
        vm.EmotionReason = null!;
        Assert.Equal(0, vm.EmotionReasonLength);
    }
}

public class TodoListAddLazyAccountTests : ViewModelTestBase
{
    private TodoListViewModel BuildVm() =>
        new(TodoRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    [Fact]
    public async Task Add_WithoutPriorLoad_LazilyFetchesAccount()
    {
        // Create account but do NOT call LoadCommand first — _accountGuid stays empty
        var account = await CreateTestAccountAsync();

        var vm = BuildVm();
        vm.NewTodoTitle = "Quick todo added without loading";

        // AddCommand fires AddAsync, which lazy-fetches _accountGuid since Load wasn't called
        await vm.AddCommand.ExecuteAsync(null);

        var todos = await TodoRepo.GetPendingAsync(account.Guid);
        Assert.Single(todos);
        Assert.Equal("Quick todo added without loading", todos[0].Title);
    }

    [Fact]
    public async Task Add_WithoutPriorLoad_NoAccount_ReturnsEarly()
    {
        // No account — lazy fetch returns null → AddAsync returns early
        var vm = BuildVm();
        vm.NewTodoTitle = "Will not be saved";

        await vm.AddCommand.ExecuteAsync(null); // should not throw

        Assert.Empty(vm.Todos);
    }
}
