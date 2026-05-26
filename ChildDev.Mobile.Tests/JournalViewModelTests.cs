using LevelUp.Data;
using LevelUp.Models;
using LevelUp.ViewModels;

namespace LevelUp.Tests;

public class JournalViewModelTests : ViewModelTestBase
{
    private JournalListViewModel BuildListVm() =>
        new(JournalRepo, AccountService, BuildOfflineSyncService(), Analytics, Nav);

    private JournalEntryViewModel BuildEntryVm() =>
        new(JournalRepo, AccountService, Analytics, Nav);

    [Fact]
    public async Task JournalList_Load_PopulatesJournals()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Great day", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Load_DoesNotRequireNetwork()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Offline entry", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Journals);
        Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task JournalList_FilterText_FiltersEntries()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Piano practice", EnteredDate = now, UpdatedOn = now });
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Running track", EnteredDate = now, UpdatedOn = now });

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "piano";

        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Delete_Confirmed_RemovesEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = now, UpdatedOn = now });

        Nav.AlertConfirmResult = true;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Empty(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Delete_Cancelled_KeepsEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await JournalRepo.SaveAsync(new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Entry", EnteredDate = now, UpdatedOn = now });

        Nav.AlertConfirmResult = false;
        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Single(vm.Journals);
    }

    [Fact]
    public async Task JournalList_Add_NavigatesToEntry()
    {
        await CreateTestAccountAsync();
        var vm = BuildListVm();
        await vm.AddCommand.ExecuteAsync(null);

        Assert.Contains("journal/entry", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalList_Open_NavigatesToEntryWithGuid()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Test", EnteredDate = now, UpdatedOn = now };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildListVm();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenCommand.ExecuteAsync(vm.Journals[0]);

        Assert.Contains(Nav.NavigatedRoutes, r => r.Contains("journal/entry") && r.Contains(journal.Guid));
    }

    [Fact]
    public void JournalEntry_CanSave_WithNotes_ReturnsTrue()
    {
        var vm = BuildEntryVm();
        vm.Notes = "Today was productive";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void JournalEntry_CanSave_WithActivity_ReturnsTrue()
    {
        var vm = BuildEntryVm();
        vm.Activity = "Soccer practice";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void JournalEntry_CanSave_EmptyNotesAndActivity_ReturnsFalse()
    {
        var vm = BuildEntryVm();
        vm.Notes = string.Empty;
        vm.Activity = string.Empty;
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task JournalEntry_Save_PersistsOffline()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Notes = "Learned something new";
        vm.Mood = "Happy";
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Learned something new", journals[0].Notes);
        Assert.Equal("Happy", journals[0].Mood);
    }

    [Fact]
    public async Task JournalEntry_Save_NavigatesBack()
    {
        await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Notes = "Good day";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalEntry_Load_PopulatesFields()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "My entry", Mood = "Calm", EnteredDate = now, UpdatedOn = now };
        await JournalRepo.SaveAsync(journal);

        var vm = BuildEntryVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);

        Assert.Equal("My entry", vm.Notes);
        Assert.Equal("Calm", vm.Mood);
        Assert.True(vm.IsExisting);
    }

    [Fact]
    public async Task JournalEntry_Delete_Confirmed_RemovesEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Delete me", EnteredDate = now, UpdatedOn = now };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = true;
        var vm = BuildEntryVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Empty(journals);
        Assert.Contains("..", Nav.NavigatedRoutes);
    }

    [Fact]
    public async Task JournalEntry_Delete_Cancelled_KeepsEntry()
    {
        var account = await CreateTestAccountAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var journal = new Journal { Guid = Guid.NewGuid().ToString(), AccountFk = account.Guid, Notes = "Keep me", EnteredDate = now, UpdatedOn = now };
        await JournalRepo.SaveAsync(journal);

        Nav.AlertConfirmResult = false;
        var vm = BuildEntryVm();
        vm.Guid = journal.Guid;
        await Task.Delay(200);
        await vm.DeleteCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
    }

    [Fact]
    public async Task JournalEntry_Save_ActivityOnly_Persists()
    {
        var account = await CreateTestAccountAsync();
        var vm = BuildEntryVm();
        vm.Activity = "Soccer practice";
        await vm.SaveCommand.ExecuteAsync(null);

        var journals = await JournalRepo.GetAllActiveAsync(account.Guid);
        Assert.Single(journals);
        Assert.Equal("Soccer practice", journals[0].Activity);
    }
}
