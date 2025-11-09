using AuthService.DTOs;
using AuthService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var users = new List<UserModel>();

// Add JWT Authentication
var key = builder.Configuration["Jwt:Key"] ?? "my_super_secret_key_454545_sfdfsdf_wedfsdfs";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Register
app.MapPost("/register", (UserDto dto) =>
{
    if (users.Any(u => u.Username == dto.Username))
        return Results.Conflict("User already exists");

    users.Add(new UserModel { Id = users.Count + 1, Username = dto.Username, Password = dto.Password });
    return Results.Ok("User registered successfully");
});

// Login
app.MapPost("/login", (UserDto dto) =>
{
    var user = users.FirstOrDefault(u => u.Username == dto.Username && u.Password == dto.Password);
    if (user == null) return Results.Unauthorized();

    var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
    var keyBytes = Encoding.UTF8.GetBytes(key);

    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("username", user.Username)
        }),
        Expires = DateTime.UtcNow.AddHours(2),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    return Results.Ok(new { token = tokenHandler.WriteToken(token) });
});

app.Run();
