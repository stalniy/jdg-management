using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.Core.Models.Sessions;
using KSeF.Client.Core.Models.Sessions.OnlineSession;
using KSeF.Client.Extensions;

namespace KSeFInvoice;

public class OnlineSessionScope(IKSeFClient client, string referenceNumber, string accessToken, EncryptionData encryptionData) : IAsyncDisposable
{
    public string ReferenceNumber { get; } = referenceNumber;
    public string AccessToken { get; } = accessToken;
    public EncryptionData EncryptionData { get; } = encryptionData;

    public async ValueTask DisposeAsync()
    {
        try
        {
            Console.WriteLine("Closing session...");
            await client.CloseOnlineSessionAsync(ReferenceNumber, AccessToken);
            Console.WriteLine("Session closed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to close session {ReferenceNumber}: {ex.Message}");
        }
    }
}

public class SendInvoiceOptions
{
    public required string BaseUrl { get; set; }
    public required string CertPath { get; set; }
    public required string KeyPath { get; set; }
    public required string KeyPassword { get; set; }
}

public class SendInvoiceResult
{
    public required string ReferenceNumber { get; set; }
    public required string VerificationUrl { get; set; }
    public string? KsefNumber { get; set; }
    public string? PollingError { get; set; }
    public string? AccessToken { get; set; }
    public string? SessionReferenceNumber { get; set; }
}

public class InvoiceSender
{
    private readonly IKSeFClient _ksefClient;
    private readonly ICryptographyService _cryptographyService;
    private readonly IVerificationLinkService _verificationLinkService;

    public InvoiceSender(
        IKSeFClient ksefClient,
        ICryptographyService cryptographyService,
        IVerificationLinkService verificationLinkService)
    {
        _ksefClient = ksefClient;
        _cryptographyService = cryptographyService;
        _verificationLinkService = verificationLinkService;
    }

    private async Task<X509Certificate2> loadCertificateAsync(string certPath, string keyPath, string keyPassword, CancellationToken ct = default)
    {
        string certPem = await File.ReadAllTextAsync(certPath, ct);
        string keyPem = await File.ReadAllTextAsync(keyPath, ct);

        X509Certificate2 publicCert = X509CertificateLoader.LoadCertificate(System.Text.Encoding.UTF8.GetBytes(certPem));
        X509Certificate2 privateKey = publicCert.MergeWithPemKey(keyPem, keyPassword);

        return privateKey;
    }

    private async Task<string> getAccessTokenAsync(string baseUrl, string nip, X509Certificate2 cert, CancellationToken ct = default)
    {
        AuthenticationChallengeResponse challenge = await _ksefClient
            .GetAuthChallengeAsync(ct);

        AuthenticationTokenRequest authTokenRequest = AuthTokenRequestBuilder
            .Create()
            .WithChallenge(challenge.Challenge)
            .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
            .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
            .Build();

        string unsignedXml = AuthenticationTokenRequestSerializer.SerializeToXmlString(authTokenRequest);
        string signedXml = SignatureService.Sign(unsignedXml, cert);

        Console.WriteLine("Submitting signed auth request...");
        SignatureResponse authSubmission = await _ksefClient
            .SubmitXadesAuthRequestAsync(signedXml, false, false, ct);

        // 3. Poll for auth status
        AuthStatus authStatus;
        DateTime startTime = DateTime.UtcNow;
        TimeSpan timeout = TimeSpan.FromMinutes(2);

        do
        {
            authStatus = await _ksefClient
                .GetAuthStatusAsync(
                    authSubmission.ReferenceNumber,
                    authSubmission.AuthenticationToken.Token,
                    ct);

            Console.WriteLine($"Auth polling: Code={authStatus.Status.Code}, Elapsed={DateTime.UtcNow - startTime:mm\\:ss}");

            if (authStatus.Status.Code != 200 && !ct.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        while (authStatus.Status.Code != 200
            && !ct.IsCancellationRequested
            && (DateTime.UtcNow - startTime) < timeout);

        if (authStatus.Status.Code != 200)
            throw new TimeoutException("Authentication timed out after 2 minutes.");

        AuthenticationOperationStatusResponse accessTokenResponse = await _ksefClient
            .GetAccessTokenAsync(authSubmission.AuthenticationToken.Token, ct);

        string accessToken = accessTokenResponse.AccessToken.Token;
        Console.WriteLine("Authenticated successfully.");
        return accessToken;
    }

    private string generateVerificationUrl(InvoiceData invoiceData, byte[] invoiceBytes)
    {
        if (string.IsNullOrWhiteSpace(invoiceData.Seller.Nip))
            throw new InvalidOperationException("Seller NIP is required to generate the KSeF verification URL.");

        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(invoiceBytes);
        string invoiceHash = Convert.ToBase64String(hashBytes);

        DateTime issueDate = DateTime.ParseExact(invoiceData.IssueDate, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
        string verificationUrl = _verificationLinkService
            .BuildInvoiceVerificationUrl(invoiceData.Seller.Nip, issueDate, invoiceHash);

        return verificationUrl;
    }

    private async Task<string?> pollForKsefNumberAsync(string referenceNumber, string sessionRef, string accessToken, CancellationToken ct = default)
    {
        string? ksefNumber = null;
        var maxAttempts = 30;
        Console.WriteLine("Polling for KSeF number...");
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var invoiceStatus = await _ksefClient
                .GetSessionInvoiceAsync(sessionRef, referenceNumber, accessToken, ct)
                ;

            Console.WriteLine($"  Attempt {attempt}/{maxAttempts}: status={invoiceStatus.Status.Code}, ksefNumber={invoiceStatus.KsefNumber ?? "(pending)"}");

            if (!string.IsNullOrEmpty(invoiceStatus.KsefNumber))
            {
                ksefNumber = invoiceStatus.KsefNumber;
                Console.WriteLine($"KSeF number assigned: {ksefNumber}");
                break;
            }
        }
        return ksefNumber;
    }

    private async Task<SendInvoiceResponse> sendInvoiceAsync(byte[] invoiceBytes, OnlineSessionScope sessionScope, string accessToken, CancellationToken ct = default)
    {
        byte[] encryptedInvoice = _cryptographyService
            .EncryptBytesWithAES256(invoiceBytes, sessionScope.EncryptionData.CipherKey, sessionScope.EncryptionData.CipherIv);

        FileMetadata invoiceMetadata = _cryptographyService.GetMetaData(invoiceBytes);
        FileMetadata encryptedMetadata = _cryptographyService.GetMetaData(encryptedInvoice);

        // 6. Send invoice
        SendInvoiceRequest sendRequest = SendInvoiceOnlineSessionRequestBuilder
            .Create()
            .WithInvoiceHash(invoiceMetadata.HashSHA, invoiceMetadata.FileSize)
            .WithEncryptedDocumentHash(encryptedMetadata.HashSHA, encryptedMetadata.FileSize)
            .WithEncryptedDocumentContent(Convert.ToBase64String(encryptedInvoice))
            .WithOfflineMode(false)
            .Build();

        SendInvoiceResponse sendResponse = await _ksefClient
            .SendOnlineSessionInvoiceAsync(sendRequest, sessionScope.ReferenceNumber, accessToken, ct)
            ;
        return sendResponse;
    }

    private async Task<OnlineSessionScope> openSessionAsync(string accessToken, CancellationToken ct = default)
    {
        EncryptionData encryptionData = _cryptographyService.GetEncryptionData();

        OpenOnlineSessionRequest openRequest = OpenOnlineSessionRequestBuilder
            .Create()
            .WithFormCode(systemCode: "FA (3)", schemaVersion: "1-0E", value: "FA")
            .WithEncryption(
                encryptedSymmetricKey: encryptionData.EncryptionInfo.EncryptedSymmetricKey,
                initializationVector: encryptionData.EncryptionInfo.InitializationVector)
            .Build();

        OpenOnlineSessionResponse session = await _ksefClient
            .OpenOnlineSessionAsync(openRequest, accessToken, cancellationToken: ct);
        Console.WriteLine($"Session opened: {session.ReferenceNumber}");

        return new OnlineSessionScope(_ksefClient, session.ReferenceNumber, accessToken, encryptionData);
    }

    public async Task<SendInvoiceResult> SendAsync(
        PreparedInvoice preparedInvoice,
        SendInvoiceOptions options,
        CancellationToken ct = default)
    {
        string? sellerNip = preparedInvoice.InvoiceData.Seller.Nip;
        if (string.IsNullOrWhiteSpace(sellerNip))
            throw new InvalidOperationException("Seller NIP is required to authenticate with KSeF.");

        Console.WriteLine("Getting auth token...");
        var cert = await loadCertificateAsync(options.CertPath, options.KeyPath, options.KeyPassword);
        var accessToken = await getAccessTokenAsync(options.BaseUrl, sellerNip, cert, ct);

        Console.WriteLine("Opening online session...");
        await using var sessionScope = await openSessionAsync(accessToken, ct);

        Console.WriteLine("Preparing invoice XML payload...");
        byte[] invoiceBytes = System.Text.Encoding.UTF8.GetBytes(preparedInvoice.InvoiceXml);

        var sendResponse = await sendInvoiceAsync(invoiceBytes, sessionScope, accessToken, ct);
        Console.WriteLine($"Invoice sent! Reference: {sendResponse.ReferenceNumber}");

        var verificationUrl = generateVerificationUrl(preparedInvoice.InvoiceData, invoiceBytes);
        Console.WriteLine($"Verification URL: {verificationUrl}");

        string? ksefNumber = null;
        string? pollingError = null;
        try
        {
            ksefNumber = await pollForKsefNumberAsync(sendResponse.ReferenceNumber, sessionScope.ReferenceNumber, accessToken, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            pollingError = ex.Message;
            Console.Error.WriteLine($"Warning: polling for the KSeF number failed after submission: {ex.Message}");
        }

        return new SendInvoiceResult
        {
            ReferenceNumber = sendResponse.ReferenceNumber,
            VerificationUrl = verificationUrl,
            KsefNumber = ksefNumber,
            PollingError = pollingError,
            AccessToken = accessToken,
            SessionReferenceNumber = sessionScope.ReferenceNumber,
        };
    }
}
