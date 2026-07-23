using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using WebLayer.Models;

namespace WebLayer.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[AllowAnonymous] // доступ без токена
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("token")]
    public IActionResult GenerateToken([FromBody] TokenRequest request)
    {
        // Проверяем клиента в списке конфигурации
        var clients = _configuration.GetSection("AuthClients:Clients").Get<List<ClientConfig>>();
        var client = clients?.FirstOrDefault(c =>
            c.ClientId == request.ClientId && c.ClientSecret == request.ClientSecret);

        if (client == null)
            return Unauthorized(new { error = "Invalid client credentials" });

        // Создаём claims
        var claims = new List<Claim>
        {
            new Claim("sender_id", client.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (client.IsAdmin)
            claims.Add(new Claim("admin", "true"));

        // Настройки JWT из конфигурации
        var jwtSection = _configuration.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(8);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expires = expires
        });
    }

    private class ClientConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public bool IsAdmin { get; set; }
    }
}