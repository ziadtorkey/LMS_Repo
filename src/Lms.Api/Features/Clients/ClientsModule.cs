namespace Lms.Api.Features.Clients;

public static class ClientsModule
{
    public static IServiceCollection AddClients(this IServiceCollection services)
    {
        services.AddScoped<CreateClientHandler>();
        return services;
    }
}