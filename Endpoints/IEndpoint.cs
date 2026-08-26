using Microsoft.AspNetCore.Routing;

namespace FrostBot.Endpoints;

public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}
