namespace AuthService.Models
{
    public class UserModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Email { get; set; } = "";
        public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }
    public class UserRole
    {
        public int Id { get; set; }
        public string Role { get; set; } = "";
    }
    public class RefreshToken
    {
        public int Id { get; set; }
        public string Token { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public bool Revoked { get; set; }
        public UserModel User { get; set; }
        public int UserId { get; set; }
    }
}
