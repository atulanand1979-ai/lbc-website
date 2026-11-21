using Azure.Data.Tables;
using Lbc.Core;
using Lbc.Core.Abstractions;
using Lbc.Core.Payments;
using Lbc.Core.Services;
using Lbc.Infrastructure.Payments;
using Lbc.Infrastructure.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lbc.Infrastructure;

public static class DependencyInjection
{
    /// <param name="storageConnectionString">Table Storage connection string ("UseDevelopmentStorage=true" for Azurite).</param>
    public static IServiceCollection AddLbc(this IServiceCollection services, IConfiguration config, string storageConnectionString)
    {
        services.Configure<PricingOptions>(config.GetSection(PricingOptions.Section));
        services.Configure<SiteOptions>(config.GetSection(SiteOptions.Section));
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<SmtpOptions>(config.GetSection(SmtpOptions.Section));
        services.Configure<PaymentOptions>(config.GetSection(PaymentOptions.Section));
        services.Configure<TurnstileOptions>(config.GetSection(TurnstileOptions.Section));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new TableServiceClient(storageConnectionString));
        services.AddSingleton<TableStore>();

        services.AddSingleton<IVenueRepository, VenueRepository>();
        services.AddSingleton<IClassRepository, ClassRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IMembershipRepository, MembershipRepository>();
        services.AddSingleton<IBookingRepository, BookingRepository>();
        services.AddSingleton<IEnquiryRepository, EnquiryRepository>();
        services.AddSingleton<IAccountTokenRepository, AccountTokenRepository>();
        services.AddSingleton<IPaymentEventLog, PaymentEventLog>();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileVerifier>();

        var provider = config[$"{PaymentOptions.Section}:Provider"] ?? "None";
        if (provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        else
            services.AddSingleton<IPaymentGateway, UnconfiguredPaymentGateway>();
        // Real providers register here, e.g. else if (provider == "Stripe") services.AddSingleton<IPaymentGateway, StripePaymentGateway>();

        services.AddScoped<AccountService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<PaymentEventProcessor>();
        services.AddScoped<EnquiryService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ScheduleAdminService>();
        services.AddScoped<MemberAdminService>();
        services.AddScoped<SeedService>();
        return services;
    }
}
