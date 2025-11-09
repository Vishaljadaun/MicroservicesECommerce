using AuthService.Data;
using AuthService.DTOs;
using AuthService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// EF
builder.Services.AddDbContext<AuthDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Password hasher
builder.Services.AddSingleton<IPasswordHasher<UserModel>, PasswordHasher<UserModel>>();


// JWT config
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        IssuerSigningKey = signingKey,
        ValidateIssuerSigningKey = true
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("UserOrAdmin", p => p.RequireRole("User", "Admin"));
});


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Database Initialization & Seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.EnsureCreated();

    // seed admin if not exists
    if (!db.Users.Any(u => u.Username == "admin"))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserModel>>();
        var admin = new UserModel { Username = "admin", Email = "admin@local" };
        admin.PasswordHash = hasher.HashPassword(admin, "Admin@123"); // change in prod
        admin.Roles.Add(new UserRole { Role = "Admin" });
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

app.UseSwagger(); 
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();


// Register endpoint
app.MapPost("/api/auth/register", async (RegisterRequest req, AuthDbContext db, IPasswordHasher<UserModel> hasher) =>
{
    if (await db.Users.AnyAsync(u => u.Username == req.Username)) return Results.Conflict("User exists");
    var user = new UserModel { Username = req.Username, Email = req.Email };
    user.PasswordHash = hasher.HashPassword(user, req.Password);
    user.Roles.Add(new UserRole { Role = "User" });
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { user.Id, user.Username });
});

// Login endpoint
app.MapPost("/api/auth/login", async (LoginRequest req, AuthDbContext db) =>
{
    var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Username == req.Username);
    if (user == null) return Results.Unauthorized();

    var hasher = app.Services.GetRequiredService<IPasswordHasher<UserModel>>();
    var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
    if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();

    // create JWT
    var claims = new List<Claim> {
        new Claim(JwtRegisteredClaimNames.Sub, user.Username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("uid", user.Id.ToString())
    };
    foreach (var r in user.Roles) claims.Add(new Claim(ClaimTypes.Role, r.Role));

    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(int.Parse(builder.Configuration["Jwt:ExpiryMinutes"] ?? "60")),
        signingCredentials: creds
    );
    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    // create refresh token (persist)
    var refreshToken = new RefreshToken { Token = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(7) };
    user.RefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync();

    return Results.Ok(new { token = tokenString, refreshToken = refreshToken.Token });
});

// Refresh endpoint
app.MapPost("/api/auth/refresh", async (RefreshRequest req, AuthDbContext db) =>
{
    var rt = await db.RefreshTokens.Include(r => r.User).FirstOrDefaultAsync(r => r.Token == req.RefreshToken);
    if (rt == null || rt.Revoked || rt.ExpiresAt < DateTime.UtcNow) return Results.Unauthorized();
    var user = rt.User!;
    // generate new JWT (same as above)
    var claims = new List<Claim> { new Claim(JwtRegisteredClaimNames.Sub, user.Username), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
    foreach (var r in user.Roles) claims.Add(new Claim(ClaimTypes.Role, r.Role));
    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(issuer: jwtIssuer, audience: jwtAudience, claims: claims, expires: DateTime.UtcNow.AddMinutes(int.Parse(builder.Configuration["Jwt:ExpiryMinutes"] ?? "60")), signingCredentials: creds);
    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = tokenString });
});

// Admin-only endpoint example (assign role)
app.MapPost("/api/auth/assign-role", [Authorize(Policy = "AdminOnly")] async (AssignRoleRequest req, AuthDbContext db) =>
{
    var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Username == req.Username);
    if (user == null) return Results.NotFound();
    if (!user.Roles.Any(r => r.Role == req.Role)) user.Roles.Add(new UserRole { Role = req.Role });
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.Run();

record RegisterRequest(string Username, string Email, string Password);
record LoginRequest(string Username, string Password);
record RefreshRequest(string RefreshToken);
record AssignRoleRequest(string Username, string Role);