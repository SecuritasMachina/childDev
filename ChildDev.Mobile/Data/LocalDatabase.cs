using LevelUp.Models;
using SQLite;

namespace LevelUp.Data;

public class LocalDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public LocalDatabase(string dbPath, string key)
    {
        SQLitePCL.Batteries_V2.Init();
        var options = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
        _db = new SQLiteAsyncConnection(options);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<Account>();
        await _db.CreateTableAsync<Journal>();
        await _db.CreateTableAsync<Goal>();
        await _db.CreateTableAsync<GoalProgress>();
        await _db.CreateTableAsync<Todo>();
        await _db.CreateTableAsync<Reminder>();
    }
}
