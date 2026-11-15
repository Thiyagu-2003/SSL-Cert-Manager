using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace CertManager.Acme.Clients;

public record AcmeChallengeItem(
    string Domain,
    string Token,
    string KeyAuthorization,
    IChallengeContext ChallengeContext);

public interface IAcmeCertificateClient
{
    Task<string> InitializeAccountAsync(string email, bool isStaging, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcmeChallengeItem>> CreateOrderAndGetChallengesAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default);
    Task<bool> SubmitChallengeAsync(AcmeChallengeItem challengeItem, CancellationToken cancellationToken = default);
    Task<byte[]> FinalizeOrderAndExportPfxAsync(string friendlyName, string password, CancellationToken cancellationToken = default);
}

public class CertesAcmeClient : IAcmeCertificateClient
{
    private AcmeContext? _context;
    private IOrderContext? _currentOrder;

    public async Task<string> InitializeAccountAsync(string email, bool isStaging, CancellationToken cancellationToken = default)
    {
        var directoryUri = isStaging 
            ? WellKnownServers.LetsEncryptStagingV2 
            : WellKnownServers.LetsEncryptV2;

        _context = new AcmeContext(directoryUri);
        var account = await _context.NewAccount(email, true);
        return account.Location.ToString();
    }

    public async Task<IReadOnlyList<AcmeChallengeItem>> CreateOrderAndGetChallengesAsync(
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken = default)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("ACME client account is not initialized. Call InitializeAccountAsync first.");
        }

        _currentOrder = await _context.NewOrder(domains.ToList());
        var authorizations = await _currentOrder.Authorizations();
        var challengeItems = new List<AcmeChallengeItem>();

        foreach (var authz in authorizations)
        {
            var authzResource = await authz.Resource();
            string domain = authzResource.Identifier.Value;
            var httpChallenge = await authz.Http();

            if (httpChallenge != null)
            {
                challengeItems.Add(new AcmeChallengeItem(
                    domain,
                    httpChallenge.Token,
                    httpChallenge.KeyAuthz,
                    httpChallenge
                ));
            }
        }

        return challengeItems;
    }

    public async Task<bool> SubmitChallengeAsync(
        AcmeChallengeItem challengeItem,
        CancellationToken cancellationToken = default)
    {
        var challengeResult = await challengeItem.ChallengeContext.Validate();
        return challengeResult.Status != ChallengeStatus.Invalid;
    }

    public async Task<byte[]> FinalizeOrderAndExportPfxAsync(
        string friendlyName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (_currentOrder == null)
        {
            throw new InvalidOperationException("No active ACME order exists.");
        }

        // Generate RSA 2048 key and finalize certificate order
        var key = KeyFactory.NewKey(KeyAlgorithm.RS256);
        var cert = await _currentOrder.Generate(new CsrInfo
        {
            CommonName = friendlyName
        }, key);

        var pfxBuilder = cert.ToPfx(key);
        return pfxBuilder.Build(friendlyName, password);
    }
}
