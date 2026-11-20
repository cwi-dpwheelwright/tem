using Cwi.Common;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.PackService.Api.Infrastructure;

public static class ServiceResponseExtensions
{
    public static IActionResult ToActionResult<T>(this ServiceResponse<T> response)
    {
        if (response.IsSuccess)
        {
            return new ObjectResult(response.Data)
            {
                StatusCode = response.StatusCode
            };
        }

        return new ObjectResult(response.Error)
        {
            StatusCode = response.StatusCode
        };
    }
}
