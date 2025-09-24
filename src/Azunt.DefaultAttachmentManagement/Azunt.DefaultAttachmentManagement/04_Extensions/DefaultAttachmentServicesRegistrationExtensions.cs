using Azunt.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azunt.DefaultAttachmentManagement;

public static class DefaultAttachmentServicesRegistrationExtensions
{
    public static void AddDependencyInjectionContainerForDefaultAttachmentApp(
        this IServiceCollection services,
        string connectionString,
        RepositoryMode mode = RepositoryMode.EfCore,
        ServiceLifetime dbContextLifetime = ServiceLifetime.Scoped)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be null or empty.", nameof(connectionString));

        switch (mode)
        {
            case RepositoryMode.EfCore:
                services.AddDbContext<DefaultAttachmentDbContext>(
                    options =>
                    {
                        options.UseSqlServer(connectionString, sql =>
                        {
                            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                            sql.CommandTimeout(60);

                            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                        });
                    },
                    dbContextLifetime);

                services.AddTransient<DefaultAttachmentDbContextFactory>(sp =>
                    new DefaultAttachmentDbContextFactory(sp.GetRequiredService<IConfiguration>()));

                services.AddScoped<IDefaultAttachmentRepository, DefaultAttachmentRepository>();
                break;

            case RepositoryMode.Dapper:
                services.AddScoped<IDefaultAttachmentRepository>(provider =>
                    new DefaultAttachmentRepositoryDapper(
                        connectionString,
                        provider.GetRequiredService<ILoggerFactory>()));
                break;

            case RepositoryMode.AdoNet:
                services.AddScoped<IDefaultAttachmentRepository>(provider =>
                    new DefaultAttachmentRepositoryAdoNet(
                        connectionString,
                        provider.GetRequiredService<ILoggerFactory>()));
                break;

            default:
                throw new InvalidOperationException(
                    $"Invalid repository mode '{mode}'. Supported modes: EfCore, Dapper, AdoNet.");
        }
    }

    public static void AddDependencyInjectionContainerForDefaultAttachmentApp(
        this IServiceCollection services,
        IConfiguration configuration,
        RepositoryMode mode = RepositoryMode.EfCore,
        ServiceLifetime dbContextLifetime = ServiceLifetime.Scoped)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("DefaultConnection is not configured properly.");

        services.AddDependencyInjectionContainerForDefaultAttachmentApp(
            connectionString,
            mode,
            dbContextLifetime);
    }

    public static void AddDependencyInjectionContainerForDefaultAttachmentAppFromSection(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName,
        RepositoryMode mode = RepositoryMode.EfCore,
        ServiceLifetime dbContextLifetime = ServiceLifetime.Scoped)
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"{connectionStringName} is not configured properly.");

        services.AddDependencyInjectionContainerForDefaultAttachmentApp(
            connectionString,
            mode,
            dbContextLifetime);
    }
}
