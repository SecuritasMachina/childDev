using ChildDev.Mobile.Models;
using SQLite;

namespace ChildDev.Mobile.Data;

public class LocalDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public LocalDatabase(string dbPath)
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<Account>();
        await _db.CreateTableAsync<Journal>();
        await _db.CreateTableAsync<Goal>();
        await _db.CreateTableAsync<GoalProgress>();
        await _db.CreateTableAsync<Todo>();
    }
}
