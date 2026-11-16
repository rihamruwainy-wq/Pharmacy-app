using Medora.Application.Features.Admin;
using Medora.Application.Features.Ai;
using Medora.Application.Features.Care;
using Medora.Application.Features.Catalog;
using Medora.Application.Features.Favorites;
using Medora.Application.Features.Identity;
using Medora.Application.Features.Orders;
using Medora.Application.Features.Pharmacist;
using Microsoft.Extensions.DependencyInjection;

namespace Medora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuthService>();
        services.AddScoped<CatalogQueries>();
        services.AddScoped<OrderService>();
        services.AddScoped<PharmacistService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<AdminInsights>();
        services.AddScoped<AdminService>();
        services.AddScoped<PharmacistCopilotService>();
        services.AddScoped<AdminAssistantService>();
        return services;
    }
}
