using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.SqlClient;

namespace AiSample.Data;

public sealed class UserRepository(SqlConnection connection, AppDbContext db)
{
    public async Task<int> CountByNameAsync(string name, CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Name = '" + name + "'", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)result!;
    }

    public string GetEmail(int id)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == id);
        return user.Email;
    }

    public static string HashPassword(string password)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    public List<User> GetActiveUsers(string country) =>
        db.Users.ToList().Where(u => u.IsActive && u.Country == country).ToList();
}
