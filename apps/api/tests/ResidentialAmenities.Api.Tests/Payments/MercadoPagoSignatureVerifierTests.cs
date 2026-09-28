using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

public sealed class MercadoPagoSignatureVerifierTests
{
    private const string Secret = "test-webhook-secret";
    private const string DataId = "ORD01JYH1Z1YJN4HZ8J3Q0RB3YP6D";
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";
    private const string Ts = "1742505638683";

    // Independent reference vectors computed with `openssl dgst -sha256 -hmac`
    // over the manifest documented by Mercado Pago:
    //   id:<data.id lowercased>;request-id:<x-request-id>;ts:<ts>;
    private const string ReferenceHash =
        "39bcbed7e05ab23ae2f86fcde921c6b79e8020f1332965ce277c6c3995b47c81";

    private const string ReferenceHashWithoutDataId =
        "fd6f57f337394293cd0b363420917183bf4b53d05007ea38f01ca6e9640ce253";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Ts)).AddSeconds(5);

    [Fact]
    public void OwnSignerMatchesIndependentOpensslVector()
    {
        Assert.Equal(
            ReferenceHash,
            WebhookSigner.Hash(Secret, WebhookSigner.Manifest(DataId, RequestId, Ts)));
    }

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            RequestId,
            DataId,
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.Valid, result);
    }

    [Fact]
    public void DataId_IsLowercasedInManifest_SoOriginalCaseStillVerifies()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            RequestId,
            DataId.ToLowerInvariant(),
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.Valid, result);
    }

    [Fact]
    public void AbsentDataId_IsOmittedFromManifest()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHashWithoutDataId}",
            RequestId,
            dataId: null,
            Secret,
            Now);

        // The signature above was computed WITHOUT the id: pair.
        Assert.Equal(SignatureVerificationResult.Valid, result);
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            RequestId,
            DataId,
            "another-secret",
            Now);

        Assert.Equal(SignatureVerificationResult.SignatureMismatch, result);
    }

    [Fact]
    public void TamperedHash_IsRejected()
    {
        var tampered = "0" + ReferenceHash[1..];

        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={tampered}",
            RequestId,
            DataId,
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.SignatureMismatch, result);
    }

    [Theory]
    [InlineData("ORDOTHERORDER000000000000001")]
    [InlineData("")]
    public void ManipulatedOrderId_IsRejected(string manipulatedDataId)
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            RequestId,
            manipulatedDataId,
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.SignatureMismatch, result);
    }

    [Fact]
    public void ManipulatedRequestId_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            "00000000-0000-0000-0000-000000000000",
            DataId,
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.SignatureMismatch, result);
    }

    [Fact]
    public void ManipulatedTimestamp_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts=1742505638684,v1={ReferenceHash}",
            RequestId,
            DataId,
            Secret,
            Now);

        Assert.Equal(SignatureVerificationResult.SignatureMismatch, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSignatureHeader_IsRejected(string? header)
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            header, RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MissingSignatureHeader, result);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("ts,v1")]
    public void MalformedSignatureHeader_IsRejected(string header)
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            header, RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MalformedSignatureHeader, result);
    }

    [Theory]
    [InlineData("ts=not-a-number")]
    [InlineData("ts=-5")]
    [InlineData("ts=17425056.38683")]
    public void MalformedTimestamp_IsRejected(string tsPart)
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"{tsPart},v1={ReferenceHash}", RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MalformedSignatureHeader, result);
    }

    [Fact]
    public void MissingTimestampComponent_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"v1={ReferenceHash}", RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MissingTimestamp, result);
    }

    [Fact]
    public void MissingHashComponent_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts}", RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MissingHash, result);
    }

    [Fact]
    public void NonHexHash_IsRejected()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1=zzzz", RequestId, DataId, Secret, Now);

        Assert.Equal(SignatureVerificationResult.MalformedSignatureHeader, result);
    }

    [Fact]
    public void MissingSecret_IsRejected_NeverAcceptedByDefault()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}", RequestId, DataId, secret: "", Now);

        Assert.Equal(SignatureVerificationResult.MissingSecret, result);
    }

    [Fact]
    public void OldTimestamp_IsRejectedWhenToleranceIsConfigured()
    {
        var result = MercadoPagoSignatureVerifier.Verify(
            $"ts={Ts},v1={ReferenceHash}",
            RequestId,
            DataId,
            Secret,
            Now.AddHours(1),
            TimeSpan.FromMinutes(10));

        Assert.Equal(SignatureVerificationResult.TimestampOutOfTolerance, result);
    }

    [Fact]
    public void SecondResolutionTimestamp_IsSupported()
    {
        const string seconds = "1704908010";
        var header = WebhookSigner.Header(Secret, DataId, RequestId, seconds);

        var result = MercadoPagoSignatureVerifier.Verify(
            header,
            RequestId,
            DataId,
            Secret,
            DateTimeOffset.FromUnixTimeSeconds(1704908010).AddSeconds(3),
            TimeSpan.FromMinutes(10));

        Assert.Equal(SignatureVerificationResult.Valid, result);
    }
}
