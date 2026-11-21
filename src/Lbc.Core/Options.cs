using Lbc.Core.Domain;

namespace Lbc.Core;

/// <summary>Prices in pence. Configured, never sent from the browser.</summary>
public sealed class PricingOptions
{
    public const string Section = "Pricing";
    public int PayAsYouGoPence { get; set; } = 750;
    public int BronzePence { get; set; } = 2500;
    public int SilverPence { get; set; } = 3500;
    public int GoldPence { get; set; } = 4500;

    public int ForTier(MembershipTier tier) => tier switch
    {
        MembershipTier.Bronze => BronzePence,
        MembershipTier.Silver => SilverPence,
        MembershipTier.Gold => GoldPence,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}

public sealed class SiteOptions
{
    public const string Section = "Site";
    /// <summary>Public base URL used in emails and gateway redirects, e.g. https://www.leicesterbhangraclasses.co.uk</summary>
    public string BaseUrl { get; set; } = "http://localhost:4280";
    public string BusinessName { get; set; } = "Leicester Bhangra Classes";
    /// <summary>Inbox that receives enquiries and admin notifications.</summary>
    public string AdminEmail { get; set; } = "info@leicesterbhangraclasses.co.uk";
    /// <summary>How many upcoming dates to offer for a Pay As You Go class.</summary>
    public int BookableWeeksAhead { get; set; } = 8;
}
