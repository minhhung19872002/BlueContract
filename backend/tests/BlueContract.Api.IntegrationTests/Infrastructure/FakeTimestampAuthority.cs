using System.Net;
using System.Net.Http.Headers;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;

namespace BlueContract.Api.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory RFC 3161 time-stamp authority (the API's "tsa" HTTP client is routed here), so signatures in the tests
/// carry a real timestamp token. <see cref="Failing"/> makes it answer 503 like an unreachable TSA.
/// </summary>
public sealed class FakeTimestampAuthority : HttpMessageHandler
{
    public const string Url = "http://tsa.test/tsr";

    private static readonly Lazy<(AsymmetricKeyParameter Key, X509Certificate Cert)> Identity = new(Create);
    private static long _serial;

    public bool Failing { get; init; }

    public static int Requests => (int)Interlocked.Read(ref _requests);

    private static long _requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        if (Failing)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var query = new TimeStampRequest(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
        var (key, cert) = Identity.Value;
        var tokens = new TimeStampTokenGenerator(key, cert, TspAlgorithms.Sha256, "1.3.6.1.4.1.99999.1");
        // Like real TSAs answering certReq=true: the token carries the TSA certificate so it can be verified.
        tokens.SetCertificates(CollectionUtilities.CreateStore(new[] { cert }));
        var generator = new TimeStampResponseGenerator(tokens, TspAlgorithms.Allowed);
        var response = generator.Generate(query, BigInteger.ValueOf(Interlocked.Increment(ref _serial)), DateTime.UtcNow);
        var content = new ByteArrayContent(response.GetEncoded());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-reply");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static (AsymmetricKeyParameter, X509Certificate) Create()
    {
        var keys = new RsaKeyPairGenerator();
        keys.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var pair = keys.GenerateKeyPair();
        var generator = new X509V3CertificateGenerator();
        var name = new X509Name("CN=TSA thử nghiệm, O=BlueProcure test, C=VN");
        generator.SetSerialNumber(BigInteger.ValueOf(DateTime.UtcNow.Ticks));
        generator.SetIssuerDN(name);
        generator.SetSubjectDN(name);
        generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generator.SetNotAfter(DateTime.UtcNow.AddYears(5));
        generator.SetPublicKey(pair.Public);
        generator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
        return (pair.Private, generator.Generate(new Asn1SignatureFactory("SHA256WITHRSA", pair.Private)));
    }
}
