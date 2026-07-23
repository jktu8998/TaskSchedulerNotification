using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebLayer.Controllers;

/// <summary>
/// Базовый класс для всех API-контроллеров.
/// Устанавливает общий префикс маршрута "api/v1/[controller]" и включает авторизацию.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
//[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
}