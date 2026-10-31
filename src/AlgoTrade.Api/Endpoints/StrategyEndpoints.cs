using AlgoTrade.Core.Domain;
using AlgoTrade.Data.Repos;
using Microsoft.AspNetCore.Mvc;

namespace AlgoTrade.Api.Endpoints;


public static class StrategyEndpoints
{
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/strategies").WithTags("Strategies");

        g.MapGet("", async (StrategyRepo repo) => Results.Json(await repo.ListAsync()));

        g.MapPost("", async (StrategyConfig s, StrategyRepo repo) =>
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                return Results.Json(new { error = "Strategy name is required." }, statusCode: 400);
            long id = await repo.InsertAsync(s);
            var created = await repo.GetAsync(id);
            return Results.Json(created);
        });

        g.MapPut("/{id:long}", async (long id, StrategyConfig s, StrategyRepo repo) =>
        {
            var updated = await repo.UpdateAsync(id, s);
            if (updated is null) return Results.NotFound(new { error = 404 });
            return Results.Json(updated);
        });

        g.MapDelete("/{id:long}", async (long id, StrategyRepo repo) =>
        {
            var ok = await repo.DeleteAsync(id);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { error = 404 });
        });

        return app;
    }
}
