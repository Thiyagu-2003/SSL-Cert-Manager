using System;
using System.Globalization;
using CertManager.Core.Enums;

namespace CertManager.Core.Common;

public record NormalizedDomainResult(
    string AsciiName,
    string DisplayName,
    string RootDomain,
    DomainType DomainType);

public static class DomainNormalizer
{
    private static readonly IdnMapping Idn = new();

    public static NormalizedDomainResult Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Domain name cannot be null or empty.", nameof(input));
        }

        string raw = input.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Domain name cannot be empty.", nameof(input));
        }

        bool isWildcard = false;
        string domainToNormalize = raw;

        if (raw.StartsWith("*.", StringComparison.Ordinal))
        {
            isWildcard = true;
            domainToNormalize = raw[2..];
        }

        if (domainToNormalize.Contains('*'))
        {
            throw new ArgumentException($"Invalid wildcard position in domain: '{input}'. Wildcards are only supported as a leading '*.' label.", nameof(input));
        }

        if (domainToNormalize.Length == 0 || !domainToNormalize.Contains('.'))
        {
            throw new ArgumentException($"Invalid domain format: '{input}'. Must contain at least one dot separating labels.", nameof(input));
        }

        string asciiBody;
        string displayBody;

        try
        {
            asciiBody = Idn.GetAscii(domainToNormalize.ToLowerInvariant());
            displayBody = Idn.GetUnicode(asciiBody);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Domain name '{input}' contains invalid characters according to IDN standards.", nameof(input), ex);
        }

        string fullAscii = isWildcard ? "*." + asciiBody : asciiBody;
        string fullDisplay = isWildcard ? "*." + displayBody : displayBody;

        // Determine root domain (last two labels, or all if only two)
        string[] labels = asciiBody.Split('.');
        string rootDomain;
        if (labels.Length <= 2)
        {
            rootDomain = asciiBody;
        }
        else
        {
            rootDomain = $"{labels[^2]}.{labels[^1]}";
        }

        DomainType domainType;
        if (isWildcard)
        {
            domainType = DomainType.Wildcard;
        }
        else if (labels.Length <= 2)
        {
            domainType = DomainType.Apex;
        }
        else
        {
            domainType = DomainType.Hostname;
        }

        return new NormalizedDomainResult(fullAscii, fullDisplay, rootDomain, domainType);
    }
}
