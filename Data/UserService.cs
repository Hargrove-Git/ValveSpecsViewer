using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

public class UserService(ValveSpecsDb db)
{
    public Task<User?> ValidateLoginAsync(string username, string password) =>
        db.Users.FirstOrDefaultAsync(u =>
            u.Username == username && u.Password == password && u.Active);

    public Task<List<User>> GetAllUsersAsync() =>
        db.Users.OrderBy(u => u.Username).ToListAsync();

    public Task<User?> GetUserAsync(int id) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<bool> UsernameTakenAsync(string username, int? excludingId = null) =>
        db.Users.AnyAsync(u => u.Username == username && u.Id != excludingId);

    public async Task CreateUserAsync(string username, string password, string displayName, string role)
    {
        db.Users.Add(new User
        {
            Username    = username,
            Password    = password,
            DisplayName = displayName,
            Role        = role,
            Active      = true,
            CreatedAt   = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task UpdateUserAsync(int id, string username, string displayName, string role, bool active, string? newPassword)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return;

        user.Username    = username;
        user.DisplayName = displayName;
        user.Role        = role;
        user.Active      = active;
        if (!string.IsNullOrWhiteSpace(newPassword))
            user.Password = newPassword;

        await db.SaveChangesAsync();
    }

    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == username && u.Password == currentPassword && u.Active);
        if (user is null) return false;

        user.Password = newPassword;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteUserAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return;

        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}
