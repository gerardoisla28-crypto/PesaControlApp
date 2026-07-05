using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.UI.Services;

public class AuthService
{
    private readonly DatabaseService _dbService;

    public AuthService(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    public async Task<User?> LoginAsync(string username, string password)
    {
        using var ctx = _dbService.CreateContext();
        return await ctx.Users
            .FirstOrDefaultAsync(u =>
                u.Username == username &&
                u.Password == password &&
                u.IsActive);
    }
}