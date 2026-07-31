using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Core.Security;

namespace PesaControl.UI.Services;

public class AuthService
{
    private readonly DatabaseService _dbService;
    private readonly IPasswordHasher _passwordHasher = new Argon2idPasswordHasher();

    public AuthService(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    public async Task<User?> LoginAsync(string username, string password)
    {
        using var ctx = _dbService.CreateContext();
        var user = await ctx.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null || !_passwordHasher.Verify(password, user.PasswordHash)) return null;
        if (_passwordHasher.NeedsRehash(user.PasswordHash)) user.PasswordHash = _passwordHasher.Hash(password);
        user.LastLogin = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
        return user;
    }
}
